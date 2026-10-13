using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kkts.DataAccess.EntityFrameworkCore;

internal static class EfModelCapabilities<T>
    where T : class
{
    internal static CapabilityResult Mapping(DbContext db, PropertyInfo binding)
    {
        var mapped = db.Model.FindEntityType(typeof(T))?.FindProperty(binding.Name);
        return mapped?.PropertyInfo == binding && mapped.ClrType == binding.PropertyType
            ? CapabilityResult.Supported
            : new(
                CapabilityStatus.Unsupported,
                "The operation requires an actual mapped scalar property."
            );
    }

    internal static CapabilityResult Ordering(DbContext db, PropertyInfo binding, bool seek)
    {
        var mapping = Mapping(db, binding);
        if (mapping.Status == CapabilityStatus.Unsupported)
            return mapping;
        var property = db.Model.FindEntityType(typeof(T))!.FindProperty(binding.Name)!;
        var type = Nullable.GetUnderlyingType(binding.PropertyType) ?? binding.PropertyType;
        if (
            seek
            && (
                property.GetValueConverter() != null
                || property.GetProviderClrType() != null
                || !(
                    type == typeof(string)
                    || type == typeof(byte)
                    || type == typeof(sbyte)
                    || type == typeof(short)
                    || type == typeof(int)
                    || type == typeof(long)
                    || type == typeof(decimal)
                    || type == typeof(DateTime)
                    || type.IsEnum
                )
            )
        )
            return new(
                CapabilityStatus.Unsupported,
                "Seek requires supported scalars without custom converters."
            );
        if (
            db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"
            && property.GetValueConverter() == null
            && property.GetProviderClrType() == null
            && type == typeof(DateTimeOffset)
        )
            return new(
                CapabilityStatus.Unsupported,
                "The installed SQLite implementation cannot order this native scalar."
            );
        if (!seek && (type == typeof(TimeSpan) || type == typeof(ulong)))
            return CapabilityResult.Unverified;
        return
            KnownProvider(db)
            && property.GetValueConverter() == null
            && property.GetProviderClrType() == null
            ? CapabilityResult.Supported
            : CapabilityResult.Unverified;
    }

    static bool KnownProvider(DbContext db) =>
        db.Database.ProviderName
            is "Microsoft.EntityFrameworkCore.Sqlite"
                or "Microsoft.EntityFrameworkCore.SqlServer"
                or "MySql.EntityFrameworkCore";

    internal static CapabilityResult Predicate(DbContext db, PropertyInfo binding, string operation)
    {
        var mapping = Mapping(db, binding);
        if (mapping.Status == CapabilityStatus.Unsupported)
            return mapping;
        var property = db.Model.FindEntityType(typeof(T))!.FindProperty(binding.Name)!;
        var type = Nullable.GetUnderlyingType(binding.PropertyType) ?? binding.PropertyType;
        if (
            db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"
            && type == typeof(DateTimeOffset)
            && operation is "lt" or "le" or "gt" or "ge"
            && property.GetValueConverter() == null
            && property.GetProviderClrType() == null
        )
            return new(
                CapabilityStatus.Unsupported,
                "SQLite cannot compare this native scalar with the requested operator."
            );
        return
            KnownProvider(db)
            && property.GetValueConverter() == null
            && property.GetProviderClrType() == null
            ? CapabilityResult.Supported
            : CapabilityResult.Unverified;
    }

    internal static CapabilityResult SnapshotShape(DbContext db, bool factoryAvailable)
    {
        if (!factoryAvailable)
            return new(
                CapabilityStatus.Unsupported,
                "A provider-owned snapshot factory is required."
            );
        if (!db.Database.IsRelational())
            return new(CapabilityStatus.Unsupported, "Snapshots require a relational provider.");
        var entity = db.Model.FindEntityType(typeof(T));
        var tableName = entity?.GetTableName();
        var table =
            tableName == null
                ? null
                : db.Model.GetRelationalModel().FindTable(tableName, entity!.GetSchema());
        if (
            entity == null
            || table == null
            || entity.GetViewName() != null
            || entity.GetSqlQuery() != null
            || entity.GetDeclaredQueryFilters().Any()
            || entity.BaseType != null
            || entity.GetDerivedTypes().Any()
            || entity.IsOwned()
            || entity.FindPrimaryKey() == null
            || entity.GetTableMappings().Count() != 1
            || table.EntityTypeMappings.Count() != 1
        )
            return new(
                CapabilityStatus.Unsupported,
                "Snapshots require an ordinary keyed single-table resource without filters or shared mappings."
            );
        if (db.Database.CreateExecutionStrategy().RetriesOnFailure)
            return new(
                CapabilityStatus.Unsupported,
                "Snapshots do not support automatic execution retries."
            );
        return KnownProvider(db) ? CapabilityResult.Supported : CapabilityResult.Unverified;
    }

    internal static void Demand(CapabilityResult result)
    {
        if (result.Status == CapabilityStatus.Unsupported)
            throw new ProviderUnsupportedException(result.Reason);
    }

    internal static void ValidatePlan(
        DbContext db,
        EntityReadPlan<T> plan,
        ReadOperations operation,
        EntitySeekPlan<T>? seek
    )
    {
        new PredicateBindings(db).Visit(plan.Predicate);
        if (operation != ReadOperations.Count)
            foreach (var binding in plan.OutputBindings)
                Demand(Mapping(db, binding.Property));
        if ((operation & (ReadOperations.Cursor | ReadOperations.Export)) != 0)
        {
            foreach (var sort in plan.Sort)
                Demand(Ordering(db, sort.Property, true));
            foreach (var binding in seek?.ContinuationBindings ?? [])
                Demand(Mapping(db, binding));
        }
        else if (operation == ReadOperations.Offset)
            foreach (var sort in plan.Sort)
                Demand(Ordering(db, sort.Property, false));
    }

    sealed class PredicateBindings(DbContext db) : ExpressionVisitor
    {
        protected override Expression VisitBinary(BinaryExpression node)
        {
            var operation = node.NodeType switch
            {
                ExpressionType.Equal => "eq",
                ExpressionType.NotEqual => "ne",
                ExpressionType.LessThan => "lt",
                ExpressionType.LessThanOrEqual => "le",
                ExpressionType.GreaterThan => "gt",
                ExpressionType.GreaterThanOrEqual => "ge",
                _ => null,
            };
            if (operation != null)
                foreach (var operand in new[] { node.Left, node.Right })
                {
                    var value = operand;
                    while (value is UnaryExpression { NodeType: ExpressionType.Convert } convert)
                        value = convert.Operand;
                    if (
                        value
                            is MemberExpression
                            {
                                Member: PropertyInfo property,
                                Expression: ParameterExpression parameter
                            }
                        && parameter.Type == typeof(T)
                    )
                        Demand(Predicate(db, property, operation));
                }
            return base.VisitBinary(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (
                node.Member is PropertyInfo binding
                && node.Expression is ParameterExpression parameter
                && parameter.Type == typeof(T)
            )
                Demand(Mapping(db, binding));
            return base.VisitMember(node);
        }
    }

    internal static ReadProviderCapabilities Profile(
        DbContext db,
        ResourceSchema<T> schema,
        ReadOperations operations,
        bool snapshotFactory
    )
    {
        var metadata = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(T));
        var fields = schema
            .Fields.Values.OrderBy(field => field.Name, StringComparer.Ordinal)
            .Select(field =>
            {
                var property = metadata?.FindProperty(field.Property.Name);
                var mapping = new ProviderMappingScope(
                    field.Property.Name,
                    field.Type.FullName!,
                    property?.GetProviderClrType()?.FullName,
                    db.Database.IsRelational() ? property?.GetColumnType() : null,
                    property?.GetValueConverter()?.GetType().FullName,
                    property?.GetPrecision(),
                    property?.GetScale(),
                    property?.GetCollation(),
                    db.Database.IsRelational() ? property?.GetColumnName() : null
                );
                var output = Mapping(db, field.Property);
                if (output.Status == CapabilityStatus.Supported && !KnownProvider(db))
                    output = CapabilityResult.Unverified;
                return new KeyValuePair<string, ProviderFieldCapabilities>(
                    field.Name,
                    new(
                        mapping,
                        output,
                        Ordering(db, field.Property, false),
                        Ordering(db, field.Property, true),
                        field.Operators.Select(op => new KeyValuePair<string, CapabilityResult>(
                            op,
                            Predicate(db, field.Property, op)
                        ))
                    )
                );
            })
            .ToArray();
        var entity = db.Model.FindEntityType(typeof(T));
        var shape = SnapshotShape(db, snapshotFactory);
        var model = System.Text.Json.JsonSerializer.Serialize(
            new
            {
                entity = entity?.Name,
                table = entity?.GetTableName(),
                schema = entity?.GetSchema(),
                view = entity?.GetViewName(),
                filters = entity?.GetDeclaredQueryFilters().Any(),
                fields = fields.Select(field => field.Value.Mapping),
                shape,
            }
        );
        var assembly = db.GetService<IDatabaseProvider>().GetType().Assembly;
        var version =
            assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unverified";
        return new(
            schema.Resource,
            schema.Version,
            db.Database.ProviderName ?? "unverified",
            version,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(model))),
            operations,
            fields,
            schema.KeyFields.Select(field => field.Name),
            shape
        );
    }
}
