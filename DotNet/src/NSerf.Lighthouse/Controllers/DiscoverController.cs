using Microsoft.AspNetCore.Mvc;
using NSerf.Lighthouse.DTOs;
using NSerf.Lighthouse.Services;

namespace NSerf.Lighthouse.Controllers;

[ApiController]
[Route("discover")]
public class DiscoverController(INodeDiscoveryService nodeDiscoveryService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> DiscoverAsync(
        [FromBody] DiscoverRequest request,
        CancellationToken cancellationToken)
    {
        var result = await nodeDiscoveryService.DiscoverNodesAsync(request, cancellationToken);

        return result.Status switch
        {
            NodeDiscoveryStatus.Success => Ok(result.Response),
            NodeDiscoveryStatus.ClusterNotFound => NotFound(new ErrorResponse { Error = result.ErrorMessage! }),
            NodeDiscoveryStatus.InvalidGuidFormat => BadRequest(new ErrorResponse { Error = result.ErrorMessage! }),
            NodeDiscoveryStatus.InvalidBase64 => BadRequest(new ErrorResponse { Error = result.ErrorMessage! }),
            NodeDiscoveryStatus.InvalidNonceSize => BadRequest(new ErrorResponse { Error = result.ErrorMessage! }),
            NodeDiscoveryStatus.PayloadTooLarge => StatusCode(413, new ErrorResponse { Error = result.ErrorMessage! }),
            NodeDiscoveryStatus.SignatureVerificationFailed => Unauthorized(new ErrorResponse { Error = result.ErrorMessage! }),
            NodeDiscoveryStatus.InvalidPayload => BadRequest(new ErrorResponse { Error = result.ErrorMessage! }),
            NodeDiscoveryStatus.ReplayAttackDetected => StatusCode(403, new ErrorResponse { Error = result.ErrorMessage! }),
            NodeDiscoveryStatus.InternalError => StatusCode(500, new ErrorResponse { Error = result.ErrorMessage! }),
            _ => StatusCode(500, new ErrorResponse { Error = "internal_error" })
        };
    }
}
