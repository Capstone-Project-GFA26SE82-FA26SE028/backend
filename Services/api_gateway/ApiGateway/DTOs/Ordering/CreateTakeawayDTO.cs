using System.Text.Json.Serialization;

namespace ApiGateway.DTOs.Ordering;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateTakeawayDTO
{
    public Guid CartId { get; set; }
    public string CartVersion { get; set; } = "";
    public string PickupName { get; set; } = "";
    public string PickupPhone { get; set; } = "";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TakeawayActionDTO
{
    public string Version { get; set; } = "";
    public string Reason { get; set; } = "";
    public string PickupCode { get; set; } = "";
    public bool HandoverConfirmed { get; set; }
    public string RefundReference { get; set; } = "";
    public string RefundEvidence { get; set; } = "";
}
