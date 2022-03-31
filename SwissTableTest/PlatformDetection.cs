// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.IO;
using System.Linq.Expressions;
using System.Security;
using System.Security.Authentication;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Microsoft.Win32;
using Xunit;

namespace System
{
    public static partial class PlatformDetection
    {
        //
        // Do not use the " { get; } = <expression> " pattern here. Having all the initialization happen in the type initializer
        // means that one exception anywhere means all tests using PlatformDetection fail. If you feel a value is worth latching,
        // do it in a way that failures don't cascade.
        //

        public static bool IsNetCore => Environment.Version.Major >= 5 || RuntimeInformation.FrameworkDescription.StartsWith(".NET Core", StringComparison.OrdinalIgnoreCase);
        public static bool IsMonoRuntime => Type.GetType("Mono.RuntimeStructs") != null;
        public static bool IsNotMonoRuntime => !IsMonoRuntime;
        public static bool IsMonoInterpreter => false;
        public static bool IsMonoAOT => Environment.GetEnvironmentVariable("MONO_AOT_MODE") == "aot";
        public static bool IsNotMonoAOT => Environment.GetEnvironmentVariable("MONO_AOT_MODE") != "aot";
        public static bool IsNativeAot => IsNotMonoRuntime && !IsReflectionEmitSupported;
        public static bool IsFreeBSD => RuntimeInformation.IsOSPlatform(OSPlatform.Create("FREEBSD"));
        public static bool IsNetBSD => RuntimeInformation.IsOSPlatform(OSPlatform.Create("NETBSD"));
        public static bool IsAndroid => RuntimeInformation.IsOSPlatform(OSPlatform.Create("ANDROID"));
        public static bool IsiOS => RuntimeInformation.IsOSPlatform(OSPlatform.Create("IOS"));
        public static bool IstvOS => RuntimeInformation.IsOSPlatform(OSPlatform.Create("TVOS"));
        public static bool IsMacCatalyst => RuntimeInformation.IsOSPlatform(OSPlatform.Create("MACCATALYST"));
        public static bool Isillumos => RuntimeInformation.IsOSPlatform(OSPlatform.Create("ILLUMOS"));
        public static bool IsSolaris => RuntimeInformation.IsOSPlatform(OSPlatform.Create("SOLARIS"));
        public static bool IsBrowser => RuntimeInformation.IsOSPlatform(OSPlatform.Create("BROWSER"));
        public static bool IsNotBrowser => !IsBrowser;
        public static bool IsMobile => IsBrowser || IsAppleMobile || IsAndroid;
        public static bool IsNotMobile => !IsMobile;
        public static bool IsAppleMobile => IsMacCatalyst || IsiOS || IstvOS;
        public static bool IsNotAppleMobile => !IsAppleMobile;

        public static bool IsArmProcess => RuntimeInformation.ProcessArchitecture == Architecture.Arm;
        public static bool IsNotArmProcess => !IsArmProcess;
        public static bool IsArm64Process => RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        public static bool IsNotArm64Process => !IsArm64Process;
        public static bool IsArmOrArm64Process => IsArmProcess || IsArm64Process;
        public static bool IsNotArmNorArm64Process => !IsArmOrArm64Process;
        public static bool IsArmv6Process => (int)RuntimeInformation.ProcessArchitecture == 7; // Architecture.Armv6
        public static bool IsX64Process => RuntimeInformation.ProcessArchitecture == Architecture.X64;
        public static bool IsX86Process => RuntimeInformation.ProcessArchitecture == Architecture.X86;
        public static bool IsNotX86Process => !IsX86Process;
        public static bool Is32BitProcess => IntPtr.Size == 4;
        public static bool Is64BitProcess => IntPtr.Size == 8;

        private static Lazy<bool> s_isCheckedRuntime => new Lazy<bool>(() => AssemblyConfigurationEquals("Checked"));
        private static Lazy<bool> s_isReleaseRuntime => new Lazy<bool>(() => AssemblyConfigurationEquals("Release"));
        private static Lazy<bool> s_isDebugRuntime => new Lazy<bool>(() => AssemblyConfigurationEquals("Debug"));

        public static bool IsCheckedRuntime => s_isCheckedRuntime.Value;
        public static bool IsReleaseRuntime => s_isReleaseRuntime.Value;
        public static bool IsDebugRuntime => s_isDebugRuntime.Value;

        // For use as needed on tests that time out when run on a Debug runtime.
        // Not relevant for timeouts on external activities, such as network timeouts.
        public static int SlowRuntimeTimeoutModifier = (PlatformDetection.IsDebugRuntime ? 5 : 1);

        public static bool IsThreadingSupported => !IsBrowser;
        public static bool IsBinaryFormatterSupported => IsNotMobile && !IsNativeAot;
        public static bool IsSymLinkSupported => !IsiOS && !IstvOS;

        public static bool IsSpeedOptimized => !IsSizeOptimized;
        public static bool IsSizeOptimized => IsBrowser || IsAndroid || IsAppleMobile;

        public static bool LocalEchoServerIsNotAvailable => !LocalEchoServerIsAvailable;
        public static bool LocalEchoServerIsAvailable => IsBrowser;

        public static bool IsUsingLimitedCultures => !IsNotMobile;
        public static bool IsNotUsingLimitedCultures => IsNotMobile;

        public static bool IsLinqExpressionsBuiltWithIsInterpretingOnly => s_linqExpressionsBuiltWithIsInterpretingOnly.Value;
        public static bool IsNotLinqExpressionsBuiltWithIsInterpretingOnly => !IsLinqExpressionsBuiltWithIsInterpretingOnly;
        private static readonly Lazy<bool> s_linqExpressionsBuiltWithIsInterpretingOnly = new Lazy<bool>(GetLinqExpressionsBuiltWithIsInterpretingOnly);
        private static bool GetLinqExpressionsBuiltWithIsInterpretingOnly()
        {
            return !(bool)typeof(LambdaExpression).GetMethod("get_CanCompileToIL").Invoke(null, Array.Empty<object>());
        }

        // Drawing is not supported on non windows platforms in .NET 7.0+.

        public static bool IsAsyncFileIOSupported => !IsBrowser;

        public static bool IsLineNumbersSupported => !IsNativeAot;
#if NETCOREAPP
        public static bool IsReflectionEmitSupported => RuntimeFeature.IsDynamicCodeSupported;
        public static bool IsNotReflectionEmitSupported => !IsReflectionEmitSupported;
#else
        public static bool IsReflectionEmitSupported => true;
#endif

        public static bool IsInvokingStaticConstructorsSupported => !IsNativeAot;

        public static bool IsMetadataUpdateSupported => !IsNativeAot;

        // System.Security.Cryptography.Xml.XmlDsigXsltTransform.GetOutput() relies on XslCompiledTransform which relies
        // heavily on Reflection.Emit

        public static bool IsPreciseGcSupported => !IsMonoRuntime;

        public static bool IsNotIntMaxValueArrayIndexSupported => s_largeArrayIsNotSupported.Value;

        public static bool IsAssemblyLoadingSupported => !IsNativeAot;
        public static bool IsMethodBodySupported => !IsNativeAot;
        public static bool IsDebuggerTypeProxyAttributeSupported => !IsNativeAot;

        private static volatile Tuple<bool> s_lazyNonZeroLowerBoundArraySupported;
        public static bool IsNonZeroLowerBoundArraySupported
        {
            get
            {
                if (s_lazyNonZeroLowerBoundArraySupported == null)
                {
                    bool nonZeroLowerBoundArraysSupported = false;
                    try
                    {
                        Array.CreateInstance(typeof(int), new int[] { 5 }, new int[] { 5 });
                        nonZeroLowerBoundArraysSupported = true;
                    }
                    catch (PlatformNotSupportedException)
                    {
                    }
                    s_lazyNonZeroLowerBoundArraySupported = Tuple.Create<bool>(nonZeroLowerBoundArraysSupported);
                }
                return s_lazyNonZeroLowerBoundArraySupported.Item1;
            }
        }

        private static volatile Tuple<bool> s_lazyMetadataTokensSupported;
        public static bool IsMetadataTokenSupported
        {
            get
            {
                if (s_lazyMetadataTokensSupported == null)
                {
                    bool metadataTokensSupported = false;
                    try
                    {
                        _ = typeof(PlatformDetection).MetadataToken;
                        metadataTokensSupported = true;
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    s_lazyMetadataTokensSupported = Tuple.Create<bool>(metadataTokensSupported);
                }
                return s_lazyMetadataTokensSupported.Item1;
            }
        }

        public static bool IsDomainJoinedMachine => !Environment.MachineName.Equals(Environment.UserDomainName, StringComparison.OrdinalIgnoreCase);
        public static bool IsNotDomainJoinedMachine => !IsDomainJoinedMachine;

        public static bool UsesMobileAppleCrypto => IsMacCatalyst || IsiOS || IstvOS;

        // Changed to `true` when linking
        public static bool IsBuiltWithAggressiveTrimming => false;
        public static bool IsNotBuiltWithAggressiveTrimming => !IsBuiltWithAggressiveTrimming;


        private static Lazy<bool> s_largeArrayIsNotSupported = new Lazy<bool>(IsLargeArrayNotSupported);

        [MethodImpl(MethodImplOptions.NoOptimization)]
        private static bool IsLargeArrayNotSupported()
        {
            try
            {
                var tmp = new byte[int.MaxValue];
                return tmp == null;
            }
            catch (OutOfMemoryException)
            {
                return true;
            }
        }


        private static readonly Lazy<bool> m_isInvariant = new Lazy<bool>(() => GetStaticNonPublicBooleanPropertyValue("System.Globalization.GlobalizationMode", "Invariant"));

        private static bool GetStaticNonPublicBooleanPropertyValue(string typeName, string propertyName)
        {
            if (Type.GetType(typeName)?.GetProperty(propertyName, BindingFlags.NonPublic | BindingFlags.Static)?.GetMethod is MethodInfo mi)
            {
                return (bool)mi.Invoke(null, null);
            }

            return false;
        }

        public static bool IsInvariantGlobalization => m_isInvariant.Value;
        public static bool IsNotInvariantGlobalization => !IsInvariantGlobalization;

        private static bool AssemblyConfigurationEquals(string configuration)
        {
            AssemblyConfigurationAttribute assemblyConfigurationAttribute = typeof(string).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>();

            return assemblyConfigurationAttribute != null &&
                string.Equals(assemblyConfigurationAttribute.Configuration, configuration, StringComparison.InvariantCulture);
        }
    }
}
