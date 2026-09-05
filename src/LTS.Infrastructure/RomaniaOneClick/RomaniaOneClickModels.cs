using System.Text.Json.Serialization;

namespace LTS.Infrastructure.RomaniaOneClick;

/// <summary>Thrown when KLG responds 429 - the caller should skip this item for the current poll rather than retry immediately.</summary>
public sealed class RomaniaRateLimitedException(string message) : Exception(message);

/// <summary>The JSON:API envelope KLG wraps every list response in - only what's needed to read attributes back out.</summary>
internal sealed class OneClickListResponseDto
{
    public List<OneClickResourceDto>? Data { get; set; }
}

internal sealed class OneClickResourceDto
{
    public string? Id { get; set; }
    public OneClickDomesticShipmentAttributesDto? Attributes { get; set; }
}

internal sealed class OneClickDomesticShipmentAttributesDto
{
    [JsonPropertyName("perm_shipment_id")]
    public string? PermShipmentId { get; set; }

    [JsonPropertyName("shipment_status")]
    public string? ShipmentStatus { get; set; }

    [JsonPropertyName("shipment_date")]
    public string? ShipmentDate { get; set; }

    [JsonPropertyName("loading_act_start_date")]
    public string? LoadingActStartDate { get; set; }

    [JsonPropertyName("unloading_start_date")]
    public string? UnloadingStartDate { get; set; }

    [JsonPropertyName("unloading_act_start_date")]
    public string? UnloadingActStartDate { get; set; }
}

/// <summary>Body of KLG's POST /api/v1/tokens/-actions/refreshToken request.</summary>
internal sealed record OneClickRefreshTokenRequestDto(OneClickRefreshTokenRequestDataDto Data)
{
    public OneClickJsonApiVersionDto Jsonapi { get; init; } = new();
}

internal sealed record OneClickJsonApiVersionDto
{
    public string Version { get; init; } = "1.0";
}

internal sealed record OneClickRefreshTokenRequestDataDto(OneClickRefreshTokenRequestAttributesDto Attributes)
{
    public string Type { get; init; } = "tokens";
}

internal sealed record OneClickRefreshTokenRequestAttributesDto(
    [property: JsonPropertyName("refresh_token")] string RefreshToken);

/// <summary>Body of KLG's refreshToken response - a flat object, not wrapped in the usual JSON:API envelope.</summary>
internal sealed class OneClickRefreshTokenResponseDto
{
    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public long ExpiresIn { get; set; }

    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }
}
