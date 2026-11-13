using ApiGateway.DTOs.AI;
using ApiGateway.Settings;
using Backend.Contracts.AI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ApiGateway.Controllers.AI;

[ApiController]
[Route("api/ai/table-sessions/{sessionId:guid}/carts/{cartId:guid}/recommendations")]
public sealed class RecommendationController(RecommendationGrpc.RecommendationGrpcClient client, IOptions<AIClientSettings> settings) : AssistantControllerBase(settings)
{
    [HttpPost]
    public Task<IActionResult> Recommend(Guid sessionId, Guid cartId, RecommendationRequestDTO body, CancellationToken ct)
    {
        return Execute(async options =>
        {
            var result = await client.RecommendAsync(new RecommendationRequest
            {
                Access = Access(sessionId),
                CartId = cartId.ToString(),
                Limit = body.Limit
            }, options);
            return new RecommendationResponseDTO(Guid.Parse(result.CartId), result.CartVersion, result.Message,
                result.Items.Select(item => new SuggestedItemDTO(Card(item.Item), item.Reason)).ToArray(),
                result.SearchLimited, DateTimeOffset.Parse(result.EvaluatedAt, System.Globalization.CultureInfo.InvariantCulture));
        }, ct, successMessage: "Lấy gợi ý món thành công.", errorMessage: VietnameseErrorMessage);
    }

    private static string VietnameseErrorMessage(int status)
    {
        return status switch
        {
            400 => "Thông tin yêu cầu gợi ý món không hợp lệ. Vui lòng kiểm tra giỏ hàng và số món gợi ý.",
            401 => "Phiên khách tại bàn không hợp lệ hoặc đã hết hạn. Vui lòng quét mã QR và tham gia bàn lại.",
            403 => "Bạn không có quyền xem gợi ý cho giỏ hàng này.",
            404 => "Không tìm thấy giỏ hàng hoặc dữ liệu món được yêu cầu.",
            409 => "Giỏ hàng hoặc trạng thái món đã thay đổi. Vui lòng tải lại giỏ hàng và thử lại.",
            413 => "Yêu cầu gợi ý món quá lớn. Vui lòng giảm dữ liệu gửi lên.",
            429 => "Trợ lý đang bận. Vui lòng thử lại sau ít phút.",
            503 => "Trợ lý hoặc dịch vụ cần thiết chưa sẵn sàng. Vui lòng thử lại hoặc hỏi nhân viên.",
            504 => "Yêu cầu gợi ý món đã quá thời gian xử lý. Vui lòng thử lại.",
            _ => "Trợ lý chưa thể tạo gợi ý món đã được xác minh. Vui lòng thử lại."
        };
    }
}
