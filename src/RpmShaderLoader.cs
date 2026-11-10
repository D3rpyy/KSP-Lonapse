// RPM stores asset-bundle shaders in JSI.JUtil's registry, outside Unity's Shader.Find lookup.
// Keep the optional integration free of an RPM assembly reference.
using System;
using System.Reflection;

namespace Lonapse
{
    public static class RpmShaderLoader
    {
        public static object Load()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type library = assembly.GetType("JSI.JUtil", false);
                if (library != null) return Load(library);
            }
            return null;
        }

        public static object Load(Type library)
        {
            MethodInfo loader = library.GetMethod("LoadInternalShader", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                                                 null, new[] { typeof(string) }, null);
            return loader != null ? loader.Invoke(null, new object[] { "RPM/FontShader" }) : null;
        }
    }
}
