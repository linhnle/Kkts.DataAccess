using System.Text.Encodings.Web;
using System.Text.Json;
using Kkts.DataAccess;
using Kkts.DataAccess.Sqlite;

namespace EmployeeApp;

public static class Designer
{
    static string E(string text) => HtmlEncoder.Default.Encode(text);

    static string Options(IEnumerable<string> values, string? selected) =>
        string.Join(
            "",
            values.Select(v =>
                $"<option{(v == selected ? " selected" : "")} value='{E(v)}'>{E(v)}</option>"
            )
        );

    static string Subjects(Subject subject) =>
        "<input class='subject-condition' type='hidden' value='"
        + E(
            subject.ContextCondition == null
                ? ""
                : JsonSerializer.Serialize(subject.ContextCondition, PolicyJson.Options)
        )
        + "'>"
        + "<label>Roles (any selected)<select class='roles' multiple>"
        + string.Join(
            "",
            new[] { "manager", "hr", "employee", "none" }.Select(r =>
                $"<option{(subject.Roles.Contains(r) ? " selected" : "")}>{r}</option>"
            )
        )
        + "</select></label><label>Groups (comma-separated names)<input class='groups' value='"
        + E(string.Join(",", subject.Groups))
        + "'></label>";

    static string Node(Condition? node, SchemaMetadata metadata)
    {
        var kind =
            node == null ? "true"
            : node.All != null ? "all"
            : node.Any != null ? "any"
            : node.Not != null ? "not"
            : "leaf";
        var start = $"<fieldset class='node' data-kind='{kind}'><legend>{kind}</legend>";
        if (kind == "true")
            return start
                + "Unconditional record match <button type='button' class='replace'>Add condition</button></fieldset>";
        if (kind is "all" or "any" or "not")
            return start
                + "<div class='children'>"
                + string.Join(
                    "",
                    (node!.All ?? node.Any ?? [node.Not!]).Select(n => Node(n, metadata))
                )
                + "</div>"
                + (
                    kind == "not"
                        ? ""
                        : "<button type='button' class='add-leaf'>Add leaf</button><button type='button' class='add-all'>Add all</button><button type='button' class='add-any'>Add any</button><button type='button' class='add-not'>Add not</button>"
                )
                + "<button type='button' class='remove'>Remove</button></fieldset>";
        var field =
            metadata.Fields.FirstOrDefault(f => f.Name == node!.Field) ?? metadata.Fields[0];
        var nullable = node!.Literal.ValueKind == JsonValueKind.Null;
        var value =
            node.Literal.ValueKind == JsonValueKind.Undefined ? ""
            : node.Literal.ValueKind == JsonValueKind.String ? node.Literal.GetString()!
            : node.Literal.GetRawText();
        return start
            + "<label>Field<select class='field'>"
            + Options(metadata.Fields.Select(f => f.Name), field.Name)
            + "</select></label><label>Operator<select class='operator'>"
            + Options(field.Operators, node.Operator)
            + "</select></label><label>Value source<select class='source'>"
            + Options(["literal", "context"], node.Context == null ? "literal" : "context")
            + "</select></label><label class='literal-label'>Typed value<input class='literal' value='"
            + E(value)
            + "'></label><label>Null<input type='checkbox' class='null'"
            + (nullable ? " checked" : "")
            + "></label><label class='context-label'>Trusted key<select class='context-key'>"
            + Options(["tenant", "user.id", "user.departmentId"], node.Context)
            + "</select></label><button type='button' class='remove'>Remove</button></fieldset>";
    }

    static string Entry(string id, Effect effect, Subject subject, string content, string css) =>
        $"<section class='{css}'><label>Id<input class='entry-id' value='{E(id)}' required></label><label>Effect<select class='effect'>{Options(["Allow", "Deny"], effect.ToString())}</select></label>"
        + Subjects(subject)
        + content
        + "<button type='button' class='remove-entry'>Remove entry</button></section>";

    public static string Render(
        SchemaMetadata metadata,
        DraftSnapshot draft,
        IReadOnlyList<AuditEvent> history,
        string token
    )
    {
        var policy =
            draft.Payload == null
                ? new PolicyDefinition(metadata.Resource, metadata.Version, [], [])
                : PolicyJson.Deserialize(draft.Payload);
        var rules = string.Join(
            "",
            policy.Rules.Select(r =>
                Entry(r.Id, r.Effect, r.Subject, Node(r.Condition, metadata), "record-rule")
            )
        );
        string Fields(FieldEntry entry) =>
            string.Join(
                "",
                Enum.GetValues<FieldOperation>()
                    .Select(op =>
                        "<fieldset><legend>"
                        + op
                        + "</legend>"
                        + string.Join(
                            "",
                            metadata
                                .Fields.Where(f => f.Eligibility.Contains(op))
                                .Select(f =>
                                    $"<label><input type='checkbox' class='grant' data-operation='{op}' value='{E(f.Name)}'{((entry.Fields.GetValueOrDefault(op) ?? []).Contains(f.Name) ? " checked" : "")}>{E(f.Name)}</label>"
                                )
                        )
                        + "</fieldset>"
                    )
            );
        var entries = string.Join(
            "",
            policy.FieldEntries.Select(f =>
                Entry(f.Id, f.Effect, f.Subject, Fields(f), "field-entry")
            )
        );
        var ruleTemplate = Entry(
            "",
            Effect.Allow,
            new([], []),
            Node(
                new()
                {
                    All =
                    [
                        new()
                        {
                            Field = "Id",
                            Operator = "eq",
                            Literal = JsonSerializer.SerializeToElement(1),
                        },
                    ],
                },
                metadata
            ),
            "record-rule"
        );
        var fieldTemplate = Entry(
            "",
            Effect.Allow,
            new([], []),
            Fields(new("", "Employee", "Read", Effect.Allow, new([], []), new())),
            "field-entry"
        );
        return $$"""
            <!doctype html><html><head><meta charset="utf-8"><title>Employee policy designer</title>
            <style>body{font:16px system-ui;max-width:1100px;margin:32px auto;padding:16px}fieldset,section{border:1px solid #bbb;padding:12px;margin:12px 0}[hidden]{display:none!important}label{display:inline-block;margin:8px}input,select,button{font:inherit;padding:6px}select[multiple]{height:100px}button{cursor:pointer}pre{white-space:pre-wrap;background:#f3f4f6;padding:16px}.node .children{margin-left:20px}</style></head><body>
            <h1>Employee policy designer</h1><p>Draft revision {{draft.DraftRevision}} · Active revision {{draft.ActiveRevision}} · Active version {{draft.ActiveVersion?.ToString()
                ?? "none (default deny)"}}</p>
            <p><a href="/login">Switch synthetic demo identity</a></p>
            <form id="designer" method="post" action="/admin/save">
            <input name="__RequestVerificationToken" type="hidden" value="{{token}}">
            <input name="draftRevision" type="hidden" value="{{draft.DraftRevision}}"><input name="activeRevision" type="hidden" value="{{draft.ActiveRevision}}"><input name="payload" id="payload" type="hidden">
            <label>Resource<select id="resource"><option>Employee</option></select></label><label>Action<select id="action"><option>Read</option></select></label>
            <h2>Record rules</h2><div id="rules">{{rules}}</div><button id="add-rule" type="button">Add record rule</button>
            <h2>Independent field permissions</h2><p>Field entries apply to subject context across authorized rows. Selecting a field grants no filtering or sorting.</p><div id="entries">{{entries}}</div><button id="add-entry" type="button">Add field entry</button>
            <p><button type="submit">Save draft</button><button type="submit" formaction="/admin/preview">Preview synthetic fixtures</button><select name="sampleRole"><option>manager</option><option>hr</option><option>employee</option><option>none</option></select></p></form>
            <form method="post" action="/admin/publish"><input type="hidden" name="__RequestVerificationToken" value="{{token}}"><input type="hidden" name="draftRevision" value="{{draft.DraftRevision}}"><input type="hidden" name="activeRevision" value="{{draft.ActiveRevision}}"><button>Publish saved draft</button></form>
            <h2>Audit history and rollback</h2>{{string.Join(
                "",
                history.Select(h =>
                    $"<p>{E(h.Time)} · {E(h.Actor)} · {E(h.Operation)} · version {h.Version} · {E(h.Summary)}</p>"
                )
            )}}
            <form method="post" action="/admin/rollback"><input type="hidden" name="__RequestVerificationToken" value="{{token}}"><input type="hidden" name="activeRevision" value="{{draft.ActiveRevision}}"><label>Published version<select name="version">{{Options(
                history.Where(h => h.Operation == "Publish").Select(h =>
                    h.Version!.Value.ToString()
                ),
                null
            )}}</select></label><button>Rollback</button></form>
            <pre id="diagnostics" role="status"></pre><template id="rule-template">{{ruleTemplate}}</template><template id="field-template">{{fieldTemplate}}</template>
            <script id="metadata" type="application/json">{{JsonSerializer.Serialize(
                metadata
            )}}</script><script src="/admin.js" defer></script></body></html>
            """;
    }
}
