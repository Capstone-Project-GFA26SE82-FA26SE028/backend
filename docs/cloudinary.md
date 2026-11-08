# Cloudinary images

Restaurant uploads generated QR PNGs to Cloudinary and persists the HTTPS URL in `RestaurantDb.dbo.Qr.ImageUrl`. Catalog uploads menu-item images through its own authorized service. Neither flow writes images to the local filesystem.

## Configuration

Set these environment variables for **Restaurant.API and Catalog.API** before starting them:

```text
Cloudinary__CloudName=your-cloud-name
Cloudinary__ApiKey=your-api-key
Cloudinary__ApiSecret=your-api-secret
```

For Docker Compose, add the following to the local, ignored `.env` file:

```text
CLOUDINARY_CLOUD_NAME=your-cloud-name
CLOUDINARY_API_KEY=your-api-key
CLOUDINARY_API_SECRET=your-api-secret
```

Do not commit credentials. Gateway does not need Cloudinary credentials. Missing credentials make image upload return HTTP 503; other operations can still run. Uploads use backend Basic authentication over HTTPS as documented in [Cloudinary's Upload API](https://cloudinary.com/documentation/upload_images). No unsigned upload preset is required.

## Database upgrade and existing QR images

For an existing local SQL Server database, execute `docker/sql/upgrades/RestaurantCloudinary.sql` against **RestaurantDb** before starting the updated Restaurant service. The script is idempotent. Docker's `db-init` applies this upgrade on the next run, and the initial schema includes the column.

Existing QR rows have a null image URL. Their tokens remain valid, but their old local PNGs are not automatically migrated. Regenerate those QR codes through the existing API to upload new images, then replace printed QR codes because regeneration rotates the token. Keep old local files/volumes until that transition is complete; this change does not delete them.

## Upload a menu-item image

1. Send `POST /api/items/images` with the existing menu-manager bearer token and `multipart/form-data`, field **file**. JPEG, PNG and WebP are supported, up to **2 MiB**; Cloudinary validates the image and allowed format.
2. Read `result.imageUrl` from the response.
3. Supply that URL as `imageUrl` in the existing JSON `POST /api/items` or `PUT /api/items/{id}` request. Updates still require the item's current `version`.

```javascript
const form = new FormData();
form.append("file", selectedFile);
const response = await fetch(`${apiBase}/api/items/images`, {
  method: "POST",
  headers: { Authorization: `Bearer ${accessToken}` },
  body: form
});
const body = await response.json();
if (!response.ok) throw new Error(body.message ?? "Image upload failed");
const imageUrl = body.result.imageUrl;
// Include imageUrl in the normal create/update item JSON request.
```

Existing HTTP(S) image URLs remain accepted for compatibility. Relative paths and `file://` URLs remain invalid.

## QR behavior

Create/regenerate QR routes and protobuf contracts are unchanged. `qr.imageUrl` and list `imageUrl` now contain a Cloudinary HTTPS URL usable directly by an `<img>`. The existing authenticated `GET /api/qr/{id}/image` route remains available and downloads the stored Cloudinary image.

QR uploads complete before the database transaction commits. Upload failure rolls back rotation and preserves the previous QR. A database commit failure after successful upload can leave an unused Cloudinary image; similarly, uploading a dish image and abandoning the form leaves an unused asset. Automatic orphan cleanup is not included. Assets are public delivery URLs; revoked QR tokens remain rejected by the resolve API even if their PNG is still available. Deleting an asset directly in Cloudinary does not clear its stored database URL.

## Verification

Automated tests use an HTTP stub or in-memory image storage, without live credentials. After configuring your Cloudinary account, smoke-test a dish upload and a QR create/regenerate, verify the returned URLs in a browser, and check that listing QR codes after a service restart retains those URLs.
