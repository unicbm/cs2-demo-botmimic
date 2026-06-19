using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Utils;
using System.Globalization;
using System.Reflection;

namespace DemoTracer;

public sealed partial class DemoTracerPlugin
{
    private sealed class RayTraceLosProbe
    {
        private const string CapabilityName = "raytrace:craytraceinterface";
        private const string ApiAssemblyName = "RayTraceApi";
        private const string RayTraceInterfaceTypeName = "RayTraceAPI.CRayTraceInterface";
        private const string TraceOptionsTypeName = "RayTraceAPI.TraceOptions";
        private const string TraceResultTypeName = "RayTraceAPI.TraceResult";
        private const string InteractionLayersTypeName = "RayTraceAPI.InteractionLayers";

        private bool _initialized;
        private object? _capability;
        private object? _traceOptions;
        private MethodInfo? _getMethod;
        private MethodInfo? _traceEndShapeMethod;
        private FieldInfo? _fractionField;
        private string _status = "unresolved";
        private DateTime _nextInitAttemptAt = DateTime.MinValue;

        public string Status
        {
            get
            {
                EnsureInitialized();
                return _status;
            }
        }

        public string ProbeStatus
        {
            get
            {
                _ = TryGetRayTrace(out _);
                return _status;
            }
        }

        public bool TryIsWorldLineClear(Vector start, Vector end, out bool clear)
        {
            clear = false;
            if (!TryGetRayTrace(out var rayTrace))
                return false;

            try
            {
                var args = new object?[] { start, end, null, _traceOptions, null };
                var hit = _traceEndShapeMethod!.Invoke(rayTrace, args) is true;
                if (!hit)
                {
                    clear = true;
                    _status = "available";
                    return true;
                }

                var result = args[4];
                if (result == null)
                {
                    _status = "bad_result";
                    return false;
                }

                var fraction = Convert.ToSingle(_fractionField!.GetValue(result), CultureInfo.InvariantCulture);
                clear = fraction >= 0.999f;
                _status = "available";
                return true;
            }
            catch
            {
                _status = "invoke_error";
                return false;
            }
        }

        private bool TryGetRayTrace(out object rayTrace)
        {
            rayTrace = null!;
            EnsureInitialized();
            if (_capability == null || _getMethod == null)
                return false;

            try
            {
                var value = _getMethod.Invoke(_capability, null);
                if (value == null)
                {
                    _status = "no_provider";
                    return false;
                }

                rayTrace = value;
                return true;
            }
            catch
            {
                _status = "get_error";
                return false;
            }
        }

        private void EnsureInitialized()
        {
            if (_initialized)
                return;
            if (DateTime.UtcNow < _nextInitAttemptAt)
                return;
            _initialized = true;

            try
            {
                var interfaceType = ResolveRayTraceType(RayTraceInterfaceTypeName);
                var optionsType = ResolveRayTraceType(TraceOptionsTypeName);
                var resultType = ResolveRayTraceType(TraceResultTypeName);
                var layersType = ResolveRayTraceType(InteractionLayersTypeName);
                if (interfaceType == null || optionsType == null || resultType == null || layersType == null)
                {
                    _status = "api_missing";
                    RetryInitializeLater();
                    return;
                }

                var capabilityType = typeof(PluginCapability<>).MakeGenericType(interfaceType);
                _capability = Activator.CreateInstance(capabilityType, CapabilityName);
                _getMethod = capabilityType.GetMethod("Get", BindingFlags.Public | BindingFlags.Instance);
                _traceEndShapeMethod = interfaceType
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(method => method.Name == "TraceEndShape" && method.GetParameters().Length == 5);
                _fractionField = resultType.GetField("Fraction", BindingFlags.Public | BindingFlags.Instance);
                _traceOptions = Activator.CreateInstance(optionsType);
                var interactsWith = optionsType.GetField("InteractsWith", BindingFlags.Public | BindingFlags.Instance);
                if (_traceOptions != null && interactsWith != null)
                {
                    var worldOnly = Enum.Parse(layersType, "MASK_WORLD_ONLY");
                    interactsWith.SetValue(_traceOptions, Convert.ToUInt64(worldOnly, CultureInfo.InvariantCulture));
                }

                if (_capability == null ||
                    _getMethod == null ||
                    _traceEndShapeMethod == null ||
                    _fractionField == null ||
                    _traceOptions == null)
                {
                    _status = "api_incomplete";
                    return;
                }

                _status = "ready";
            }
            catch
            {
                _status = "init_error";
                RetryInitializeLater();
            }
        }

        private void RetryInitializeLater()
        {
            _initialized = false;
            _nextInitAttemptAt = DateTime.UtcNow.AddSeconds(1.0);
        }

        private static Type? ResolveRayTraceType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var existing = assembly.GetType(fullName, throwOnError: false);
                if (existing != null)
                    return existing;
            }

            try
            {
                return Assembly.Load(new AssemblyName(ApiAssemblyName)).GetType(fullName, throwOnError: false);
            }
            catch
            {
                return Type.GetType($"{fullName}, {ApiAssemblyName}", throwOnError: false);
            }
        }
    }
}
