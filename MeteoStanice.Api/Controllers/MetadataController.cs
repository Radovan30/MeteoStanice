using MeteoStanice.Contracts.Statuses;
using Microsoft.AspNetCore.Mvc;

namespace MeteoStanice.Api.Controllers;

[ApiController]
[Route("api/metadata")]
public sealed class MetadataController : ControllerBase
{
    /// <summary>
    /// Returns stable firmware status codes and display labels for all clients.
    /// </summary>
    [HttpGet("statuses")]
    public ActionResult<FirmwareStatusMetadataDto> GetStatuses()
        => FirmwareStatusCatalog.CreateMetadata();
}
