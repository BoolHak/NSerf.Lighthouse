using Microsoft.AspNetCore.Mvc;
using NSerf.Lighthouse.DTOs;
using NSerf.Lighthouse.Services;

namespace NSerf.Lighthouse.Controllers;

[ApiController]
[Route("clusters")]
public class ClustersController(IClusterService clusterService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> RegisterClusterAsync(
        [FromBody] RegisterClusterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await clusterService.RegisterClusterAsync(request, cancellationToken);

        return result.Status switch
        {
            ClusterRegistrationStatus.Created => StatusCode(201),
            ClusterRegistrationStatus.AlreadyExists => Ok(),
            ClusterRegistrationStatus.PublicKeyMismatch => Conflict(new ErrorResponse { Error = result.ErrorMessage! }),
            ClusterRegistrationStatus.InvalidGuidFormat => BadRequest(new ErrorResponse { Error = result.ErrorMessage! }),
            ClusterRegistrationStatus.InvalidPublicKey => BadRequest(new ErrorResponse { Error = result.ErrorMessage! }),
            _ => StatusCode(500, new ErrorResponse { Error = "internal_error" })
        };
    }
}
