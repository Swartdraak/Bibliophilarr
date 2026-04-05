using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Matching;
using Microsoft.Extensions.DependencyInjection;

namespace Bibliophilarr.Http.Validation
{
    public class DuplicateEndpointDetector
    {
        private readonly IServiceProvider _services;

        public DuplicateEndpointDetector(IServiceProvider services)
        {
            _services = services;
        }

        public Dictionary<string, List<string>> GetDuplicateEndpoints(EndpointDataSource dataSource)
        {
            // get the DfaMatcherBuilder - internal, so needs reflection
            var matcherBuilderType = typeof(IEndpointSelectorPolicy).Assembly
                .GetType("Microsoft.AspNetCore.Routing.Matching.DfaMatcherBuilder");

            var rawBuilder = _services.GetRequiredService(matcherBuilderType);

            var addEndpointMethod = matcherBuilderType.GetMethod("AddEndpoint");
            var buildDfaTreeMethod = matcherBuilderType.GetMethod("BuildDfaTree");

            var endpoints = dataSource.Endpoints;
            foreach (var t in endpoints)
            {
                if (t is RouteEndpoint endpoint && (endpoint.Metadata.GetMetadata<ISuppressMatchingMetadata>()?.SuppressMatching ?? false) == false)
                {
                    addEndpointMethod.Invoke(rawBuilder, new object[] { endpoint });
                }
            }

            var visited = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
            var duplicates = new Dictionary<string, List<string>>();

            var rawTree = buildDfaTreeMethod.Invoke(rawBuilder, new object[] { true });

            Visit(rawTree, LogDuplicates);

            return duplicates;

            void LogDuplicates(object node)
            {
                if (!visited.TryGetValue(node, out var label))
                {
                    label = visited.Count;
                    visited.Add(node, label);
                }

                var matches = GetProperty<List<Endpoint>>(node, "Matches");
                var nodeLabel = GetProperty<string>(node, "Label");

                var filteredMatches = matches?.Where(x => !x.DisplayName.StartsWith("Bibliophilarr.Http.Frontend.StaticResourceController")).ToList();
                var matchCount = filteredMatches?.Count ?? 0;
                if (matchCount > 1)
                {
                    var duplicateEndpoints = filteredMatches.Select(x => x.DisplayName).ToList();
                    duplicates[nodeLabel] = duplicateEndpoints;
                }
            }
        }

        private static void Visit(object rawNode, Action<object> visitor)
        {
            var literals = GetProperty<IDictionary>(rawNode, "Literals");
            if (literals?.Values != null)
            {
                foreach (var dictValue in literals.Values)
                {
                    Visit(dictValue, visitor);
                }
            }

            var parameters = GetProperty<object>(rawNode, "Parameters");
            if (parameters != null && !ReferenceEquals(rawNode, parameters))
            {
                Visit(parameters, visitor);
            }

            var catchAll = GetProperty<object>(rawNode, "CatchAll");
            if (catchAll != null && !ReferenceEquals(rawNode, catchAll))
            {
                Visit(catchAll, visitor);
            }

            var policyEdges = GetProperty<IDictionary>(rawNode, "PolicyEdges");
            if (policyEdges?.Values != null)
            {
                foreach (var dictValue in policyEdges.Values)
                {
                    Visit(dictValue, visitor);
                }
            }

            visitor(rawNode);
        }

        private static T GetProperty<T>(object obj, string name)
        {
            var prop = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null)
            {
                return default;
            }

            return (T)prop.GetValue(obj);
        }
    }
}
