using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Xtce.Workshop.Model;
using Xtce.Workshop.Validation;

namespace Xtce.Workshop.Api.Controllers;

/// <summary>
/// Item-granular access to a server-held document (#129): browse the tree lazily,
/// fetch one item, replace one item, validate, search, and save — no full-document
/// JSON ever crosses the wire.
/// </summary>
[ApiController]
[Route("api/xtce/sessions")]
public sealed class DocumentSessionsController : ControllerBase
{
    private static readonly JsonSerializerOptions ItemJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly DocumentSessionService _sessions;

    public DocumentSessionsController(DocumentSessionService sessions)
    {
        _sessions = sessions;
    }

    /// <summary>One system node, summarised: child-system names and per-kind item counts.</summary>
    [HttpGet("{id}/node")]
    public IActionResult Node(string id, [FromQuery] string? path)
    {
        if (_sessions.Get(id) is not { } session)
        {
            return NotFound(new { error = "Unknown or expired document session." });
        }
        SpaceSystem? system;
        lock (session.Gate)
        {
            system = DocumentItems.Resolve(session.Document, path);
        }
        if (system is null)
        {
            return NotFound(new { error = "No system at that path." });
        }
        return Ok(new
        {
            name = system.Name,
            childSystems = system.Children.Select(c => c.Name).ToList(),
            groups = DocumentItems.Kinds.ToDictionary(
                kind => kind,
                kind => DocumentItems.ItemsOf(system, kind)?.Count ?? 0),
        });
    }

    /// <summary>A page of item names for one kind on one system.</summary>
    [HttpGet("{id}/items")]
    public IActionResult Items(
        string id, [FromQuery] string? path, [FromQuery] string kind,
        [FromQuery] int offset = 0, [FromQuery] int limit = 200)
    {
        if (_sessions.Get(id) is not { } session)
        {
            return NotFound(new { error = "Unknown or expired document session." });
        }
        if (!DocumentItems.Kinds.Contains(kind))
        {
            return BadRequest(new { error = $"Unknown item kind '{kind}'." });
        }
        List<string> names;
        int total;
        lock (session.Gate)
        {
            if (DocumentItems.Resolve(session.Document, path) is not { } system)
            {
                return NotFound(new { error = "No system at that path." });
            }
            var items = DocumentItems.ItemsOf(system, kind) ?? [];
            total = items.Count;
            names = items.Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 1000))
                .Select(DocumentItems.NameOf).ToList();
        }
        return Ok(new { total, offset = Math.Max(0, offset), names });
    }

    /// <summary>One item, in the same JSON shape the full-document load uses.</summary>
    [HttpGet("{id}/item")]
    public IActionResult Item(string id, [FromQuery] string? path, [FromQuery] string kind, [FromQuery] int index)
    {
        if (_sessions.Get(id) is not { } session)
        {
            return NotFound(new { error = "Unknown or expired document session." });
        }
        object? item;
        lock (session.Gate)
        {
            if (DocumentItems.Resolve(session.Document, path) is not { } system)
            {
                return NotFound(new { error = "No system at that path." });
            }
            var items = DocumentItems.ItemsOf(system, kind);
            if (items is null || index < 0 || index >= items.Count)
            {
                return NotFound(new { error = "No item at that kind/index." });
            }
            item = items[index];
        }
        return Ok(item);
    }

    /// <summary>
    /// Replaces one item wholesale (renames included). The body is the item's JSON in
    /// the same shape Item returns; the server-held model stays the source of truth.
    /// </summary>
    [HttpPut("{id}/item")]
    public async Task<IActionResult> ReplaceItem(string id, [FromQuery] string? path, [FromQuery] string kind, [FromQuery] int index)
    {
        if (_sessions.Get(id) is not { } session)
        {
            return NotFound(new { error = "Unknown or expired document session." });
        }
        if (DocumentItems.ClrType(kind) is not { } clrType)
        {
            return BadRequest(new { error = $"Unknown item kind '{kind}'." });
        }
        object? replacement;
        try
        {
            replacement = await JsonSerializer.DeserializeAsync(Request.Body, clrType, ItemJson);
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = $"The item body did not deserialize: {ex.Message}" });
        }
        if (replacement is null)
        {
            return BadRequest(new { error = "The request body was empty." });
        }
        lock (session.Gate)
        {
            if (DocumentItems.Resolve(session.Document, path) is not { } system)
            {
                return NotFound(new { error = "No system at that path." });
            }
            var items = DocumentItems.ItemsOf(system, kind);
            if (items is null || index < 0 || index >= items.Count)
            {
                return NotFound(new { error = "No item at that kind/index." });
            }
            session.Document = DocumentItems.UpdateAt(session.Document, path, node =>
            {
                var list = DocumentItems.ItemsOf(node, kind)!.ToList();
                list[index] = replacement;
                return DocumentItems.WithList(node, kind, list);
            });
        }
        return NoContent();
    }

    /// <summary>Runs every validation rule against the held model.</summary>
    [HttpPost("{id}/validate")]
    public IActionResult Validate(string id)
    {
        if (_sessions.Get(id) is not { } session)
        {
            return NotFound(new { error = "Unknown or expired document session." });
        }
        SpaceSystem document;
        lock (session.Gate)
        {
            document = session.Document;
        }
        return Ok(new { validationIssues = XtceValidator.Validate(document) });
    }

    /// <summary>Name/alias search across the held model.</summary>
    [HttpGet("{id}/search")]
    public IActionResult Search(string id, [FromQuery] string query)
    {
        if (_sessions.Get(id) is not { } session)
        {
            return NotFound(new { error = "Unknown or expired document session." });
        }
        SpaceSystem document;
        lock (session.Gate)
        {
            document = session.Document;
        }
        return Ok(new { matches = XtceDocumentQuery.Search(document, query) });
    }

    /// <summary>Serializes the held model to XTCE XML — losslessness lives server-side.</summary>
    [HttpGet("{id}/save")]
    public IActionResult Save(string id)
    {
        if (_sessions.Get(id) is not { } session)
        {
            return NotFound(new { error = "Unknown or expired document session." });
        }
        SpaceSystem document;
        lock (session.Gate)
        {
            document = session.Document;
        }
        return Content(XtceDocumentWriter.Write(document), "application/xml");
    }

    [HttpDelete("{id}")]
    public IActionResult Drop(string id) =>
        _sessions.Drop(id) ? NoContent() : NotFound(new { error = "Unknown or expired document session." });
}
