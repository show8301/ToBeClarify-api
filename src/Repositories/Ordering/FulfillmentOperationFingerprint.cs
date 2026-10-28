using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Repositories.Ordering;

internal static class FulfillmentOperationFingerprint
{
    public static string Create(string unitId, FulfillmentTransitionRequest request)
    {
        // Keep the original single-unit hash when the newly added flag is absent.
        var payload = JsonSerializer.Serialize(new {
            unitId, request.Action, request.Quantity, request.Reason, request.ScheduledStartsAt,
            request.TargetBusinessPeriodId, request.RestMinutes, request.CompensationAmount,
            request.CompensationReason, request.ActualStartsAt, request.ActualEndsAt, request.ExpectedVersion
        }) + (request.StillInProgress ? "|still_in_progress" : "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
