using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using QRCoder;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;
using Restaurant.Common.Settings;
using Restaurant.DAL.Models;
using Restaurant.DAL.Interfaces;

namespace Restaurant.BLL.Implements;

public sealed class QrService(IQrRepository repository, ITableRepository tables,
    IManagementTransaction transaction, IManagementAccess access, IOptions<QrSettings> settings, IQrImageStore images) : IQrService
{
    public async Task<PageResult<QrDto>> GetAll(ListQuery query, CancellationToken ct)
    {
        await access.RequireAdmin(ct);
        ManagementValidation.Query(query);
        var page = await repository.GetPageAsync(query, ct);
        return new PageResult<QrDto>
        {
            Items = page.Items.Select(Map).ToList(),
            TotalCount = page.TotalCount,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public async Task<byte[]> GetImage(Guid id, CancellationToken ct)
    {
        await access.RequireAdmin(ct);
        var qr = await repository.GetByIdAsync(id, ct);
        if (qr is null)
        {
            throw new ManagementException(404, "QR code not found.");
        }

        if (string.IsNullOrWhiteSpace(qr.ImageUrl))
        {
            throw new ManagementException(404, "No Cloudinary image is available. Regenerate the QR code.");
        }

        return await images.ReadAsync(qr.ImageUrl, ct);
    }

    public Task<QrCreatedDto> Create(Guid tableId, CancellationToken ct)
    {
        return Issue(tableId, null, ct);
    }

    public Task<QrCreatedDto> Regenerate(Guid tableId, Guid expectedQrId, CancellationToken ct)
    {
        if (expectedQrId == Guid.Empty)
        {
            throw new ManagementException(400, "ExpectedQrId must identify the current QR code.");
        }

        return Issue(tableId, expectedQrId, ct);
    }

    private async Task<QrCreatedDto> Issue(Guid tableId, Guid? expectedQrId, CancellationToken ct)
    {
        await access.RequireAdmin(ct);
        if (tableId == Guid.Empty)
        {
            throw new ManagementException(400, "Invalid TableId.");
        }

        if (!Uri.TryCreate(settings.Value.MenuBaseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != "http" && baseUri.Scheme != "https")
            || !string.IsNullOrEmpty(baseUri.Fragment) || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.UserInfo) || settings.Value.MenuBaseUrl.Length > 1024)
        {
            throw new ManagementException(503, "Qr:MenuBaseUrl must be an HTTP/HTTPS URL without a query or fragment.");
        }

        return await transaction.ExecuteAsync(async () =>
        {
            var table = await tables.GetByIdAsync(tableId, ct)
                ?? throw new ManagementException(404, "Table not found.");
            if (!table.IsActive || table.Area is null || !table.Area.IsActive)
            {
                throw new ManagementException(409, "The table and its area must be active.");
            }

            var active = await repository.GetActiveAsync(tableId, ct);
            if (!expectedQrId.HasValue && active.Count > 0)
            {
                throw new ManagementException(409, "The table already has a QR code. Regenerate it using the current QR code ID.");
            }

            if (expectedQrId.HasValue && !active.Any(x => x.QrId == expectedQrId.Value))
            {
                throw new ManagementException(409, "The QR code has changed or belongs to another table. Reload the list.");
            }

            var now = DateTime.UtcNow;
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            // Table code in the fragment is only a display hint. Resolve always uses the token.
            var url = baseUri.AbsoluteUri + "#qr=" + token + "&table=" + Uri.EscapeDataString(table.TableCode);
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            using var png = new PngByteQRCode(data);
            var image = png.GetGraphic(8);
            foreach (var old in active)
            {
                old.RevokedAt = now;
            }

            var entity = new Qr
            {
                QrId = Guid.NewGuid(),
                TableId = tableId,
                Table = table,
                CreatedAt = now,
                TokenHash = Hash(token)
            };
            // Upload before commit so a failed upload cannot revoke the current QR.
            entity.ImageUrl = await images.SaveAsync(entity.QrId, image, ct);
            repository.Add(entity);
            return new QrCreatedDto
            {
                Qr = Map(entity),
                MenuUrl = url,
                Token = token,
                Png = image
            };
        }, ct);
    }

    public async Task<QrTableDto> Resolve(string token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token) || token.Length != 64 || !token.All(Uri.IsHexDigit))
        {
            throw new ManagementException(404, "The QR code is invalid or has been revoked.");
        }

        var qr = await repository.FindByHashAsync(Hash(token), ct);
        if (qr is null || !qr.Table.IsActive || !qr.Table.Area.IsActive)
        {
            throw new ManagementException(404, "The QR code is invalid or has been revoked.");
        }

        return new QrTableDto
        {
            TableId = qr.TableId,
            TableCode = qr.Table.TableCode,
            AreaId = qr.Table.AreaId,
            RestaurantId = qr.Table.Area.RestaurantId
        };
    }

    private static string Hash(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    private QrDto Map(Qr qr)
    {
        return new QrDto
        {
            QrId = qr.QrId,
            ImageUrl = qr.ImageUrl,
            TableId = qr.TableId,
            TableCode = qr.Table.TableCode,
            AreaId = qr.Table.AreaId,
            CreatedAt = DateTime.SpecifyKind(qr.CreatedAt, DateTimeKind.Utc),
            RevokedAt = qr.RevokedAt.HasValue ? DateTime.SpecifyKind(qr.RevokedAt.Value, DateTimeKind.Utc) : null,
            IsUsable = qr.RevokedAt is null && qr.Table.IsActive && qr.Table.Area.IsActive
        };
    }
}
