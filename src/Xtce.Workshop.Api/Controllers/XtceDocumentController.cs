using Microsoft.AspNetCore.Mvc;
using Xtce.Workshop.Model;
using Xtce.Workshop.Validation;

namespace Xtce.Workshop.Api.Controllers;

[ApiController]
[Route("api/xtce")]
public sealed class XtceDocumentController : ControllerBase
{
    /// <summary>Loads an uploaded XTCE file into the editable document model.</summary>
    [HttpPost("load")]
    public async Task<IActionResult> Load(IFormFile file)
    {
        await using var stream = file.OpenReadStream();

        try
        {
            var spaceSystem = XtceDocumentReader.Load(stream);
            var tree = TreeNode.FromSpaceSystem(spaceSystem);
            var validationIssues = XtceValidator.Validate(spaceSystem);
            return Ok(new { name = spaceSystem.Name, tree, document = spaceSystem, validationIssues });
        }
        catch (XtceParseException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Writes the document back out as XTCE XML.</summary>
    [HttpPost("save")]
    public ContentResult Save([FromBody] SpaceSystem spaceSystem)
    {
        var xml = XtceDocumentWriter.Write(XtceDocumentNormalizer.Normalize(spaceSystem));
        return Content(xml, "application/xml");
    }

    /// <summary>Runs every validation rule against the document.</summary>
    [HttpPost("validate")]
    public IActionResult Validate([FromBody] SpaceSystem spaceSystem)
    {
        var validationIssues = XtceValidator.Validate(XtceDocumentNormalizer.Normalize(spaceSystem));
        return Ok(new { validationIssues });
    }
}
