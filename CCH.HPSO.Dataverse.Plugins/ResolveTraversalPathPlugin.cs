using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace CCH.HPSO.Dataverse.Plugins
{
    /// <summary>
    /// Backing plugin for the custom API ms_ResolveTraversalPath.
    ///
    /// Request parameters:
    ///   PlaceholderName (String) - placeholder key on the mapping table, e.g. "{tournament.host.name}"
    ///   EntityName      (String) - logical name of the pivot record's table (e.g. "ms_tournament")
    ///   EntityGuid      (String) - GUID of the pivot record
    ///   ValueMode       (String) - optional: "Formatted" (default) or "Raw"
    ///
    /// The plugin looks up ms_prompttemplateattributemapping by ms_placeholdername to obtain the
    /// ms_traversalpath, then resolves that path starting from the supplied pivot record.
    ///
    /// Response properties:
    ///   Result          (String)  - JSON: scalar when one value, flat array when many, "null" when
    ///                               no data is available (no mapping row, no configured path, or the
    ///                               path resolves to no records/empty attribute)
    ///   IsCollection    (Boolean) - true when more than one value was found
    ///   ValueCount      (Integer) - number of leaf values
    ///   TraversalPath   (String)  - the traversal path that was resolved (echoed for transparency)
    /// </summary>
    public sealed class ResolveTraversalPathPlugin : IPlugin
    {
        private const string MappingEntity = "ms_prompttemplateattributemapping";
        private const string PlaceholderAttr = "ms_placeholdername";
        private const string TraversalPathAttr = "ms_traversalpath";

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var trace = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));

            // Run under the calling user's context so row-level security is honored.
            var service = factory.CreateOrganizationService(context.UserId);

            string placeholderName = GetInputString(context, "PlaceholderName");
            string entityName = GetInputString(context, "EntityName");
            string entityGuidRaw = GetInputString(context, "EntityGuid");
            string valueMode = GetInputString(context, "ValueMode");

            if (string.IsNullOrWhiteSpace(placeholderName))
                throw new InvalidPluginExecutionException("PlaceholderName is required.");
            if (string.IsNullOrWhiteSpace(entityName))
                throw new InvalidPluginExecutionException("EntityName is required.");
            if (!Guid.TryParse(entityGuidRaw, out var entityGuid))
                throw new InvalidPluginExecutionException("EntityGuid must be a valid GUID.");

            bool formatted = !string.Equals(valueMode, "Raw", StringComparison.OrdinalIgnoreCase);

            try
            {
                string path = LookupTraversalPath(service, placeholderName);

                // A placeholder may have no mapping/traversal path configured. In that case
                // (and whenever the configured path resolves to no data) the API returns a
                // null result instead of failing, so the caller can continue processing the
                // remaining placeholders.
                if (string.IsNullOrWhiteSpace(path))
                {
                    trace?.Trace("No traversal path configured for placeholder '{0}'. Returning null.", placeholderName);
                    SetNullResult(context, path);
                    return;
                }

                var resolver = new TraversalPathResolver(service, trace);
                var result = resolver.Resolve(entityName, entityGuid, path, formatted);

                context.OutputParameters["Result"] = result.Json;
                context.OutputParameters["IsCollection"] = result.IsCollection;
                context.OutputParameters["ValueCount"] = result.Count;
                context.OutputParameters["TraversalPath"] = path;
            }
            catch (InvalidPluginExecutionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                trace?.Trace("ResolveTraversalPath error: {0}", ex);
                throw new InvalidPluginExecutionException("ResolveTraversalPath failed: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Resolves the traversal path for a placeholder by reading the mapping table.
        /// Returns null when the placeholder has no mapping row or no configured path
        /// (treated by the caller as "no data" -> null result). Still throws when the
        /// placeholder is genuinely misconfigured to multiple different paths.
        /// </summary>
        private static string LookupTraversalPath(IOrganizationService service, string placeholderName)
        {
            var query = new QueryExpression(MappingEntity)
            {
                ColumnSet = new ColumnSet(TraversalPathAttr),
                TopCount = 10
            };
            query.Criteria.AddCondition(PlaceholderAttr, ConditionOperator.Equal, placeholderName);

            var rows = service.RetrieveMultiple(query).Entities;
            if (rows.Count == 0)
                return null;   // No mapping configured for this placeholder -> treated as "no data".

            var paths = rows
                .Select(e => e.GetAttributeValue<string>(TraversalPathAttr))
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (paths.Count == 0)
                return null;   // Mapping exists but no traversal path -> treated as "no data".
            if (paths.Count > 1)
                throw new InvalidPluginExecutionException(
                    "Placeholder '" + placeholderName + "' is ambiguous: it maps to multiple different traversal paths (" +
                    string.Join(", ", paths) + ").");

            return paths[0];
        }

        /// <summary>
        /// Emits a null result on the output parameters (used when no data is available).
        /// </summary>
        private static void SetNullResult(IPluginExecutionContext context, string path)
        {
            context.OutputParameters["Result"] = "null";
            context.OutputParameters["IsCollection"] = false;
            context.OutputParameters["ValueCount"] = 0;
            context.OutputParameters["TraversalPath"] = path ?? string.Empty;
        }

        private static string GetInputString(IPluginExecutionContext context, string key)
        {
            return context.InputParameters.TryGetValue(key, out var v) && v != null
                ? v.ToString()
                : null;
        }
    }
}
