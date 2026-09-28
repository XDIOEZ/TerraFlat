using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace PuertsUnityMcp
{
    public interface IUnityMcpToolProvider
    {
        string EndpointKind { get; }
        void RegisterTools(UnityMcpToolProviderContext context);
    }

    public sealed class UnityMcpToolProviderContext
    {
        private readonly List<string> registeredToolNames = new List<string>();

        public UnityMcpToolProviderContext(IUnityMcpEndpoint endpoint, UnityMcpToolRegistry registry)
        {
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public IUnityMcpEndpoint Endpoint { get; }
        public UnityMcpToolRegistry Registry { get; }
        public string EndpointId => Endpoint.EndpointId;
        public string EndpointKind => Endpoint.EndpointKind;
        public string EndpointName => Endpoint.EndpointName;
        public IReadOnlyList<string> RegisteredToolNames => registeredToolNames;

        public void Register(IUnityMcpTool tool)
        {
            if (!TryRegister(tool))
            {
                throw new InvalidOperationException("MCP tool already exists: " + (tool == null ? string.Empty : tool.Name));
            }
        }

        public bool TryRegister(IUnityMcpTool tool)
        {
            if (!Registry.TryRegister(tool))
            {
                return false;
            }

            registeredToolNames.Add(tool.Name);
            return true;
        }
    }

    public sealed class UnityMcpToolProviderDiscoveryResult
    {
        public int providerCount;
        public int registeredToolCount;
        public string[] registeredToolNames = new string[0];
        public string[] errors = new string[0];
    }

    public static class UnityMcpToolProviderDiscovery
    {
        public static UnityMcpToolProviderDiscoveryResult RegisterLoadedAssemblyProviders(IUnityMcpEndpoint endpoint, UnityMcpToolRegistry registry)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            var registeredToolNames = new List<string>();
            var errors = new List<string>();
            var providerCount = 0;
            var providerType = typeof(IUnityMcpToolProvider);
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            for (var assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
            {
                var assembly = assemblies[assemblyIndex];
                if (assembly == null || assembly.IsDynamic)
                {
                    continue;
                }

                var types = GetTypesSafely(assembly, errors);
                for (var typeIndex = 0; typeIndex < types.Length; typeIndex++)
                {
                    var type = types[typeIndex];
                    if (type == null
                        || type.IsAbstract
                        || type.IsInterface
                        || type.ContainsGenericParameters
                        || !providerType.IsAssignableFrom(type))
                    {
                        continue;
                    }

                    try
                    {
                        var provider = Activator.CreateInstance(type) as IUnityMcpToolProvider;
                        if (provider == null || !SupportsEndpoint(provider.EndpointKind, endpoint.EndpointKind))
                        {
                            continue;
                        }

                        var context = new UnityMcpToolProviderContext(endpoint, registry);
                        provider.RegisterTools(context);
                        providerCount++;
                        for (var i = 0; i < context.RegisteredToolNames.Count; i++)
                        {
                            registeredToolNames.Add(context.RegisteredToolNames[i]);
                        }
                    }
                    catch (Exception ex)
                    {
                        var message = "C# MCP tool provider failed: " + type.FullName + ": " + ex.Message;
                        errors.Add(message);
                        Debug.LogWarning("[UnityMCP] " + message);
                    }
                }
            }

            return new UnityMcpToolProviderDiscoveryResult
            {
                providerCount = providerCount,
                registeredToolCount = registeredToolNames.Count,
                registeredToolNames = registeredToolNames.ToArray(),
                errors = errors.ToArray()
            };
        }

        private static Type[] GetTypesSafely(Assembly assembly, List<string> errors)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                if (ex.LoaderExceptions != null)
                {
                    for (var i = 0; i < ex.LoaderExceptions.Length; i++)
                    {
                        if (ex.LoaderExceptions[i] != null)
                        {
                            errors.Add("Assembly type load warning: " + assembly.FullName + ": " + ex.LoaderExceptions[i].Message);
                        }
                    }
                }

                return ex.Types ?? new Type[0];
            }
            catch (Exception ex)
            {
                errors.Add("Assembly scan warning: " + assembly.FullName + ": " + ex.Message);
                return new Type[0];
            }
        }

        private static bool SupportsEndpoint(string providerEndpointKind, string endpointKind)
        {
            if (string.IsNullOrEmpty(providerEndpointKind)
                || string.Equals(providerEndpointKind, "all", StringComparison.OrdinalIgnoreCase)
                || string.Equals(providerEndpointKind, "*", StringComparison.Ordinal))
            {
                return true;
            }

            var parts = providerEndpointKind.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i].Trim();
                if (string.Equals(part, endpointKind, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(part, "runtime", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(endpointKind, "player", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(part, "player", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(endpointKind, "runtime", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
