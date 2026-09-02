using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace CCH.HPSO.Dataverse.Plugins
{
    /// <summary>
    /// Resolves a dotted traversal path (e.g. "contact.account.tournament.ms_name")
    /// starting from a single record, walking N:1 / 1:N / N:N relationships purely
    /// from metadata, and returning the leaf attribute value(s) as a flat set.
    /// </summary>
    public sealed class TraversalPathResolver
    {
        private const int MaxFrontier = 5000;   // guardrail against runaway fan-out
        private const int InBatchSize = 500;    // ids per IN query

        private readonly IOrganizationService _service;
        private readonly ITracingService _trace;
        private readonly Dictionary<string, EntityMetadata> _metaCache =
            new Dictionary<string, EntityMetadata>(StringComparer.OrdinalIgnoreCase);

        public TraversalPathResolver(IOrganizationService service, ITracingService trace)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _trace = trace;
        }

        public ResolveResult Resolve(string startEntity, Guid startId, string path, bool formatted)
        {
            if (string.IsNullOrWhiteSpace(startEntity))
                throw new InvalidPluginExecutionException("StartEntity is required.");
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidPluginExecutionException("TraversalPath is required.");

            var tokens = path.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
                             .Select(t => t.Trim())
                             .Where(t => t.Length > 0)
                             .ToList();
            if (tokens.Count == 0)
                throw new InvalidPluginExecutionException("TraversalPath is empty.");

            // Optional leading entity name (e.g. "contact.account...."): validate & drop it.
            if (string.Equals(tokens[0], startEntity, StringComparison.OrdinalIgnoreCase))
                tokens.RemoveAt(0);
            if (tokens.Count == 0)
                throw new InvalidPluginExecutionException("TraversalPath must end with an attribute name.");

            string attribute = tokens[tokens.Count - 1];
            var hops = tokens.Take(tokens.Count - 1).ToList();

            string curEntity = startEntity;
            List<Guid> frontier = new List<Guid> { startId };

            foreach (var token in hops)
            {
                var hop = ResolveHop(curEntity, token);
                _trace?.Trace("Hop '{0}': {1} [{2}] -> {3} (frontier={4})",
                    token, curEntity, hop.Kind, hop.TargetEntity, frontier.Count);

                switch (hop.Kind)
                {
                    case HopKind.ManyToOne:
                        frontier = TraverseManyToOne(curEntity, hop, frontier);
                        break;
                    case HopKind.OneToMany:
                        frontier = TraverseOneToMany(hop, frontier);
                        break;
                    case HopKind.ManyToMany:
                        frontier = TraverseManyToMany(hop, frontier);
                        break;
                }

                curEntity = hop.TargetEntity;
                if (frontier.Count == 0) break;
                if (frontier.Count > MaxFrontier)
                    throw new InvalidPluginExecutionException(
                        $"Traversal fan-out exceeded {MaxFrontier} records at entity '{curEntity}'.");
            }

            var values = frontier.Count == 0
                ? new List<object>()
                : ReadAttributeValues(curEntity, attribute, frontier, formatted);

            return new ResolveResult(values);
        }

        // ---------- Hop resolution (metadata-driven, generalized) ----------

        private Hop ResolveHop(string curEntity, string token)
        {
            var meta = GetMeta(curEntity);

            // 1) Relationship schema name (most explicit / unambiguous).
            var m2o = meta.ManyToOneRelationships.FirstOrDefault(r => Eq(r.SchemaName, token));
            if (m2o != null) return Hop.FromManyToOne(m2o);

            var o2m = meta.OneToManyRelationships.FirstOrDefault(r => Eq(r.SchemaName, token));
            if (o2m != null) return Hop.FromOneToMany(o2m);

            var m2m = meta.ManyToManyRelationships.FirstOrDefault(r => Eq(r.SchemaName, token));
            if (m2m != null) return Hop.FromManyToMany(m2m, curEntity);

            // 2) Lookup attribute name on the current entity (N:1).
            var byLookup = meta.ManyToOneRelationships
                               .Where(r => Eq(r.ReferencingAttribute, token))
                               .ToList();
            if (byLookup.Count == 1) return Hop.FromManyToOne(byLookup[0]);
            if (byLookup.Count > 1) throw Ambiguous(token, curEntity, byLookup.Select(r => r.SchemaName));

            // 3) Target entity logical name reachable by exactly one relationship.
            var candidates = new List<Hop>();
            candidates.AddRange(meta.ManyToOneRelationships
                .Where(r => Eq(r.ReferencedEntity, token)).Select(Hop.FromManyToOne));
            candidates.AddRange(meta.OneToManyRelationships
                .Where(r => Eq(r.ReferencingEntity, token)).Select(Hop.FromOneToMany));
            candidates.AddRange(meta.ManyToManyRelationships
                .Where(r => Eq(OtherManyToManyEntity(r, curEntity), token))
                .Select(r => Hop.FromManyToMany(r, curEntity)));

            if (candidates.Count == 1) return candidates[0];
            if (candidates.Count == 0)
                throw new InvalidPluginExecutionException(
                    $"No relationship, lookup, or reachable entity named '{token}' was found on '{curEntity}'.");

            throw Ambiguous(token, curEntity, candidates.Select(c => c.SchemaName));
        }

        // ---------- Traversal per relationship type ----------

        private List<Guid> TraverseManyToOne(string curEntity, Hop hop, List<Guid> frontier)
        {
            var pk = GetMeta(curEntity).PrimaryIdAttribute;
            var result = new HashSet<Guid>();
            foreach (var batch in Batches(frontier, InBatchSize))
            {
                var qe = new QueryExpression(curEntity) { ColumnSet = new ColumnSet(hop.ReferencingAttribute) };
                qe.Criteria.AddCondition(pk, ConditionOperator.In, batch.Cast<object>().ToArray());
                foreach (var e in RetrieveAll(qe))
                {
                    var er = e.GetAttributeValue<EntityReference>(hop.ReferencingAttribute);
                    if (er != null) result.Add(er.Id);
                }
            }
            return result.ToList();
        }

        private List<Guid> TraverseOneToMany(Hop hop, List<Guid> frontier)
        {
            var childPk = GetMeta(hop.TargetEntity).PrimaryIdAttribute;
            var result = new HashSet<Guid>();
            foreach (var batch in Batches(frontier, InBatchSize))
            {
                var qe = new QueryExpression(hop.TargetEntity) { ColumnSet = new ColumnSet(false) };
                qe.Criteria.AddCondition(hop.ReferencingAttribute, ConditionOperator.In, batch.Cast<object>().ToArray());
                foreach (var e in RetrieveAll(qe)) result.Add(e.Id);
            }
            return result.ToList();
        }

        private List<Guid> TraverseManyToMany(Hop hop, List<Guid> frontier)
        {
            var otherPk = GetMeta(hop.TargetEntity).PrimaryIdAttribute;
            var result = new HashSet<Guid>();
            foreach (var batch in Batches(frontier, InBatchSize))
            {
                var qe = new QueryExpression(hop.TargetEntity) { ColumnSet = new ColumnSet(false) };
                var link = qe.AddLink(hop.IntersectEntity, otherPk, hop.OtherIntersectAttribute);
                link.LinkCriteria.AddCondition(hop.SelfIntersectAttribute, ConditionOperator.In, batch.Cast<object>().ToArray());
                foreach (var e in RetrieveAll(qe)) result.Add(e.Id);
            }
            return result.ToList();
        }

        // ---------- Leaf attribute read ----------

        private List<object> ReadAttributeValues(string entity, string attribute, List<Guid> frontier, bool formatted)
        {
            var pk = GetMeta(entity).PrimaryIdAttribute;
            var values = new List<object>();
            foreach (var batch in Batches(frontier, InBatchSize))
            {
                var qe = new QueryExpression(entity) { ColumnSet = new ColumnSet(attribute) };
                qe.Criteria.AddCondition(pk, ConditionOperator.In, batch.Cast<object>().ToArray());
                foreach (var e in RetrieveAll(qe))
                {
                    if (!e.Contains(attribute) || e[attribute] == null) continue;
                    values.Add(FormatValue(e, attribute, formatted));
                }
            }
            return values;
        }

        private static object FormatValue(Entity e, string attr, bool formatted)
        {
            var raw = e[attr];
            if (raw is AliasedValue av) raw = av.Value;

            if (formatted && e.FormattedValues.Contains(attr))
                return e.FormattedValues[attr];

            switch (raw)
            {
                case EntityReference er: return formatted ? (object)(er.Name ?? er.Id.ToString()) : er.Id.ToString();
                case OptionSetValue osv: return osv.Value;
                case Money mo: return mo.Value;
                case bool b: return b;
                case int i: return i;
                case long l: return l;
                case decimal m: return m;
                case double d: return d;
                case DateTime dt: return dt;
                case Guid g: return g.ToString();
                default: return raw?.ToString();
            }
        }

        // ---------- Helpers ----------

        private EntityMetadata GetMeta(string logicalName)
        {
            if (_metaCache.TryGetValue(logicalName, out var cached)) return cached;
            var resp = (RetrieveEntityResponse)_service.Execute(new RetrieveEntityRequest
            {
                LogicalName = logicalName,
                EntityFilters = EntityFilters.Entity | EntityFilters.Relationships,
                RetrieveAsIfPublished = true
            });
            _metaCache[logicalName] = resp.EntityMetadata;
            return resp.EntityMetadata;
        }

        private IEnumerable<Entity> RetrieveAll(QueryExpression qe)
        {
            qe.PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 };
            while (true)
            {
                var ec = _service.RetrieveMultiple(qe);
                foreach (var e in ec.Entities) yield return e;
                if (!ec.MoreRecords) break;
                qe.PageInfo.PageNumber++;
                qe.PageInfo.PagingCookie = ec.PagingCookie;
            }
        }

        private static IEnumerable<List<Guid>> Batches(List<Guid> source, int size)
        {
            for (int i = 0; i < source.Count; i += size)
                yield return source.GetRange(i, Math.Min(size, source.Count - i));
        }

        private static string OtherManyToManyEntity(ManyToManyRelationshipMetadata r, string cur)
            => Eq(r.Entity1LogicalName, cur) ? r.Entity2LogicalName : r.Entity1LogicalName;

        private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static InvalidPluginExecutionException Ambiguous(string token, string entity, IEnumerable<string> options)
            => new InvalidPluginExecutionException(
                $"Hop '{token}' on '{entity}' is ambiguous. Use one of these relationship schema names instead: {string.Join(", ", options)}.");

        // ---------- Hop model ----------

        private enum HopKind { ManyToOne, OneToMany, ManyToMany }

        private sealed class Hop
        {
            public HopKind Kind;
            public string SchemaName;
            public string TargetEntity;
            public string ReferencingAttribute;      // M2O: lookup on current; 1:N: lookup on child
            public string IntersectEntity;           // M2M
            public string SelfIntersectAttribute;    // M2M: intersect attr for current side
            public string OtherIntersectAttribute;   // M2M: intersect attr for target side

            public static Hop FromManyToOne(OneToManyRelationshipMetadata r) => new Hop
            {
                Kind = HopKind.ManyToOne,
                SchemaName = r.SchemaName,
                TargetEntity = r.ReferencedEntity,
                ReferencingAttribute = r.ReferencingAttribute
            };

            public static Hop FromOneToMany(OneToManyRelationshipMetadata r) => new Hop
            {
                Kind = HopKind.OneToMany,
                SchemaName = r.SchemaName,
                TargetEntity = r.ReferencingEntity,
                ReferencingAttribute = r.ReferencingAttribute
            };

            public static Hop FromManyToMany(ManyToManyRelationshipMetadata r, string curEntity)
            {
                bool curIsEntity1 = string.Equals(r.Entity1LogicalName, curEntity, StringComparison.OrdinalIgnoreCase);
                return new Hop
                {
                    Kind = HopKind.ManyToMany,
                    SchemaName = r.SchemaName,
                    TargetEntity = curIsEntity1 ? r.Entity2LogicalName : r.Entity1LogicalName,
                    IntersectEntity = r.IntersectEntityName,
                    SelfIntersectAttribute = curIsEntity1 ? r.Entity1IntersectAttribute : r.Entity2IntersectAttribute,
                    OtherIntersectAttribute = curIsEntity1 ? r.Entity2IntersectAttribute : r.Entity1IntersectAttribute
                };
            }
        }
    }

    /// <summary>Flat result of a traversal: scalar when one value, JSON array when many.</summary>
    public sealed class ResolveResult
    {
        public ResolveResult(List<object> values)
        {
            Count = values.Count;
            IsCollection = values.Count > 1;
            if (values.Count == 0) Json = "null";
            else if (values.Count == 1) Json = JsonUtil.WriteScalar(values[0]);
            else Json = JsonUtil.WriteArray(values);
        }

        public string Json { get; }
        public bool IsCollection { get; }
        public int Count { get; }
    }
}
