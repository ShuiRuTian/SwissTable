using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace System.Collections.Generic
{
    public partial class MyDictionary<TKey, TValue>
    {
        [StackTraceHidden]
        internal static class ThrowHelper
        {

            [DoesNotReturn]
            internal static void ThrowSerializationException(ExceptionResource resource)
            {
                throw new SerializationException(GetResourceString(resource));
            }

            [DoesNotReturn]
            internal static void ThrowKeyNotFoundException<T>(T key)
            {
                // Generic key to move the boxing to the right hand side of throw
                throw GetKeyNotFoundException((object?)key);
            }

            [DoesNotReturn]
            internal static void ThrowArgumentException(ExceptionResource resource)
            {
                throw GetArgumentException(resource);
            }

            [DoesNotReturn]
            internal static void ThrowArgumentOutOfRangeException()
            {
                throw new ArgumentOutOfRangeException();
            }

            [DoesNotReturn]
            internal static void ThrowAddingDuplicateWithKeyArgumentException<T>(T key)
            {
                // Generic key to move the boxing to the right hand side of throw
                throw GetAddingDuplicateWithKeyArgumentException((object?)key);
            }

            [DoesNotReturn]
            internal static void ThrowArgumentNullException(ExceptionArgument argument)
            {
                throw new ArgumentNullException(GetArgumentName(argument));
            }

            [DoesNotReturn]
            internal static void ThrowNotSupportedException(ExceptionResource resource)
            {
                throw new NotSupportedException(GetResourceString(resource));
            }

            [DoesNotReturn]
            internal static void ThrowIndexArgumentOutOfRange_NeedNonNegNumException()
            {
                throw GetArgumentOutOfRangeException(ExceptionArgument.index,
                                                        ExceptionResource.ArgumentOutOfRange_NeedNonNegNum);
            }


            [DoesNotReturn]
            internal static void ThrowArgumentException_Argument_InvalidArrayType()
            {
                throw new ArgumentException(SR.Argument_InvalidArrayType);
            }

            [DoesNotReturn]
            internal static void ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen()
            {
                throw new InvalidOperationException(SR.InvalidOperation_EnumOpCantHappen);
            }

            [DoesNotReturn]
            internal static void ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion()
            {
                throw new InvalidOperationException(SR.InvalidOperation_EnumFailedVersion);
            }

            // Allow nulls for reference types and Nullable<U>, but not for value types.
            // Aggressively inline so the jit evaluates the if in place and either drops the call altogether
            // Or just leaves null test and call to the Non-returning ThrowHelper.ThrowArgumentNullException
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static void IfNullAndNullsAreIllegalThenThrow<T>(object? value, ExceptionArgument argName)
            {
                // Note that default(T) is not equal to null for value types except when T is Nullable<U>.
                if (!(default(T) == null) && value == null)
                    ThrowHelper.ThrowArgumentNullException(argName);
            }


            [DoesNotReturn]
            internal static void ThrowWrongKeyTypeArgumentException<T>(T key, Type targetType)
            {
                // Generic key to move the boxing to the right hand side of throw
                throw GetWrongKeyTypeArgumentException((object?)key, targetType);
            }

            [DoesNotReturn]
            internal static void ThrowWrongValueTypeArgumentException<T>(T value, Type targetType)
            {
                // Generic key to move the boxing to the right hand side of throw
                throw GetWrongValueTypeArgumentException((object?)value, targetType);
            }

            private static ArgumentException GetAddingDuplicateWithKeyArgumentException(object? key)
            {
                return new ArgumentException(SR.Format(SR.Argument_AddingDuplicateWithKey, key));
            }

            [DoesNotReturn]
            internal static void ThrowArgumentOutOfRangeException(ExceptionArgument argument)
            {
                throw new ArgumentOutOfRangeException(GetArgumentName(argument));
            }

            private static ArgumentException GetArgumentException(ExceptionResource resource)
            {
                return new ArgumentException(GetResourceString(resource));
            }


            private static ArgumentException GetWrongKeyTypeArgumentException(object? key, Type targetType)
            {
                return new ArgumentException(SR.Format(SR.Arg_WrongType, key, targetType), nameof(key));
            }

            private static ArgumentException GetWrongValueTypeArgumentException(object? value, Type targetType)
            {
                return new ArgumentException(SR.Format(SR.Arg_WrongType, value, targetType), nameof(value));
            }

            private static KeyNotFoundException GetKeyNotFoundException(object? key)
            {
                return new KeyNotFoundException(SR.Format(SR.Arg_KeyNotFoundWithKey, key));
            }

            private static ArgumentOutOfRangeException GetArgumentOutOfRangeException(ExceptionArgument argument, ExceptionResource resource)
            {
                return new ArgumentOutOfRangeException(GetArgumentName(argument), GetResourceString(resource));
            }

            private static string GetResourceString(ExceptionResource resource)
            {
                switch (resource)
                {
                    case ExceptionResource.NotSupported_KeyCollectionSet:
                        return SR.NotSupported_KeyCollectionSet;
                    case ExceptionResource.NotSupported_ValueCollectionSet:
                        return SR.NotSupported_ValueCollectionSet;
                    case ExceptionResource.Arg_RankMultiDimNotSupported:
                        return SR.Arg_RankMultiDimNotSupported;
                    case ExceptionResource.Arg_NonZeroLowerBound:
                        return SR.Arg_NonZeroLowerBound;
                    case ExceptionResource.Arg_ArrayPlusOffTooSmall:
                        return SR.Arg_ArrayPlusOffTooSmall;
                    case ExceptionResource.Serialization_MissingKeys:
                        return SR.Serialization_MissingKeys;
                    case ExceptionResource.Serialization_NullKey:
                        return SR.Serialization_NullKey;
                    case ExceptionResource.ArgumentOutOfRange_NeedNonNegNum:
                        return SR.ArgumentOutOfRange_NeedNonNegNum;
                    default:
                        Debug.Fail("The enum value is not defined, please check the ExceptionResource Enum.");
                        return "";
                }
            }

            private static string GetArgumentName(ExceptionArgument argument)
            {
                switch (argument)
                {
                    case ExceptionArgument.obj:
                        return "obj";
                    case ExceptionArgument.dictionary:
                        return "dictionary";
                    case ExceptionArgument.array:
                        return "array";
                    case ExceptionArgument.info:
                        return "info";
                    case ExceptionArgument.key:
                        return "key";
                    case ExceptionArgument.text:
                        return "text";
                    case ExceptionArgument.values:
                        return "values";
                    case ExceptionArgument.value:
                        return "value";
                    case ExceptionArgument.startIndex:
                        return "startIndex";
                    case ExceptionArgument.task:
                        return "task";
                    case ExceptionArgument.bytes:
                        return "bytes";
                    case ExceptionArgument.byteIndex:
                        return "byteIndex";
                    case ExceptionArgument.byteCount:
                        return "byteCount";
                    case ExceptionArgument.ch:
                        return "ch";
                    case ExceptionArgument.chars:
                        return "chars";
                    case ExceptionArgument.charIndex:
                        return "charIndex";
                    case ExceptionArgument.charCount:
                        return "charCount";
                    case ExceptionArgument.s:
                        return "s";
                    case ExceptionArgument.input:
                        return "input";
                    case ExceptionArgument.ownedMemory:
                        return "ownedMemory";
                    case ExceptionArgument.list:
                        return "list";
                    case ExceptionArgument.index:
                        return "index";
                    case ExceptionArgument.capacity:
                        return "capacity";
                    case ExceptionArgument.collection:
                        return "collection";
                    case ExceptionArgument.item:
                        return "item";
                    case ExceptionArgument.converter:
                        return "converter";
                    case ExceptionArgument.match:
                        return "match";
                    case ExceptionArgument.count:
                        return "count";
                    case ExceptionArgument.action:
                        return "action";
                    case ExceptionArgument.comparison:
                        return "comparison";
                    case ExceptionArgument.exceptions:
                        return "exceptions";
                    case ExceptionArgument.exception:
                        return "exception";
                    case ExceptionArgument.pointer:
                        return "pointer";
                    case ExceptionArgument.start:
                        return "start";
                    case ExceptionArgument.format:
                        return "format";
                    case ExceptionArgument.formats:
                        return "formats";
                    case ExceptionArgument.culture:
                        return "culture";
                    case ExceptionArgument.comparer:
                        return "comparer";
                    case ExceptionArgument.comparable:
                        return "comparable";
                    case ExceptionArgument.source:
                        return "source";
                    case ExceptionArgument.state:
                        return "state";
                    case ExceptionArgument.length:
                        return "length";
                    case ExceptionArgument.comparisonType:
                        return "comparisonType";
                    case ExceptionArgument.manager:
                        return "manager";
                    case ExceptionArgument.sourceBytesToCopy:
                        return "sourceBytesToCopy";
                    case ExceptionArgument.callBack:
                        return "callBack";
                    case ExceptionArgument.creationOptions:
                        return "creationOptions";
                    case ExceptionArgument.function:
                        return "function";
                    case ExceptionArgument.scheduler:
                        return "scheduler";
                    case ExceptionArgument.continuationAction:
                        return "continuationAction";
                    case ExceptionArgument.continuationFunction:
                        return "continuationFunction";
                    case ExceptionArgument.tasks:
                        return "tasks";
                    case ExceptionArgument.asyncResult:
                        return "asyncResult";
                    case ExceptionArgument.beginMethod:
                        return "beginMethod";
                    case ExceptionArgument.endMethod:
                        return "endMethod";
                    case ExceptionArgument.endFunction:
                        return "endFunction";
                    case ExceptionArgument.cancellationToken:
                        return "cancellationToken";
                    case ExceptionArgument.continuationOptions:
                        return "continuationOptions";
                    case ExceptionArgument.delay:
                        return "delay";
                    case ExceptionArgument.millisecondsDelay:
                        return "millisecondsDelay";
                    case ExceptionArgument.millisecondsTimeout:
                        return "millisecondsTimeout";
                    case ExceptionArgument.stateMachine:
                        return "stateMachine";
                    case ExceptionArgument.timeout:
                        return "timeout";
                    case ExceptionArgument.type:
                        return "type";
                    case ExceptionArgument.sourceIndex:
                        return "sourceIndex";
                    case ExceptionArgument.sourceArray:
                        return "sourceArray";
                    case ExceptionArgument.destinationIndex:
                        return "destinationIndex";
                    case ExceptionArgument.destinationArray:
                        return "destinationArray";
                    case ExceptionArgument.pHandle:
                        return "pHandle";
                    case ExceptionArgument.handle:
                        return "handle";
                    case ExceptionArgument.other:
                        return "other";
                    case ExceptionArgument.newSize:
                        return "newSize";
                    case ExceptionArgument.lowerBounds:
                        return "lowerBounds";
                    case ExceptionArgument.lengths:
                        return "lengths";
                    case ExceptionArgument.len:
                        return "len";
                    case ExceptionArgument.keys:
                        return "keys";
                    case ExceptionArgument.indices:
                        return "indices";
                    case ExceptionArgument.index1:
                        return "index1";
                    case ExceptionArgument.index2:
                        return "index2";
                    case ExceptionArgument.index3:
                        return "index3";
                    case ExceptionArgument.length1:
                        return "length1";
                    case ExceptionArgument.length2:
                        return "length2";
                    case ExceptionArgument.length3:
                        return "length3";
                    case ExceptionArgument.endIndex:
                        return "endIndex";
                    case ExceptionArgument.elementType:
                        return "elementType";
                    case ExceptionArgument.arrayIndex:
                        return "arrayIndex";
                    case ExceptionArgument.year:
                        return "year";
                    case ExceptionArgument.codePoint:
                        return "codePoint";
                    case ExceptionArgument.str:
                        return "str";
                    case ExceptionArgument.options:
                        return "options";
                    case ExceptionArgument.prefix:
                        return "prefix";
                    case ExceptionArgument.suffix:
                        return "suffix";
                    case ExceptionArgument.buffer:
                        return "buffer";
                    case ExceptionArgument.buffers:
                        return "buffers";
                    case ExceptionArgument.offset:
                        return "offset";
                    case ExceptionArgument.stream:
                        return "stream";
                    case ExceptionArgument.anyOf:
                        return "anyOf";
                    case ExceptionArgument.overlapped:
                        return "overlapped";
                    default:
                        Debug.Fail("The enum value is not defined, please check the ExceptionArgument Enum.");
                        return "";
                }
            }

#if false // Reflection-based implementation does not work for CoreRT/ProjectN
        // This function will convert an ExceptionResource enum value to the resource string.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string GetResourceString(ExceptionResource resource)
        {
            Debug.Assert(Enum.IsDefined(typeof(ExceptionResource), resource),
                "The enum value is not defined, please check the ExceptionResource Enum.");

            return SR.GetResourceString(resource.ToString());
        }
#endif
        }

        //
        // The convention for this enum is using the argument name as the enum name
        //
        internal enum ExceptionArgument
        {
            obj,
            dictionary,
            array,
            info,
            key,
            text,
            values,
            value,
            startIndex,
            task,
            bytes,
            byteIndex,
            byteCount,
            ch,
            chars,
            charIndex,
            charCount,
            s,
            input,
            ownedMemory,
            list,
            index,
            capacity,
            collection,
            item,
            converter,
            match,
            count,
            action,
            comparison,
            exceptions,
            exception,
            pointer,
            start,
            format,
            formats,
            culture,
            comparer,
            comparable,
            source,
            state,
            length,
            comparisonType,
            manager,
            sourceBytesToCopy,
            callBack,
            creationOptions,
            function,
            scheduler,
            continuationAction,
            continuationFunction,
            tasks,
            asyncResult,
            beginMethod,
            endMethod,
            endFunction,
            cancellationToken,
            continuationOptions,
            delay,
            millisecondsDelay,
            millisecondsTimeout,
            stateMachine,
            timeout,
            type,
            sourceIndex,
            sourceArray,
            destinationIndex,
            destinationArray,
            pHandle,
            handle,
            other,
            newSize,
            lowerBounds,
            lengths,
            len,
            keys,
            indices,
            index1,
            index2,
            index3,
            length1,
            length2,
            length3,
            endIndex,
            elementType,
            arrayIndex,
            year,
            codePoint,
            str,
            options,
            prefix,
            suffix,
            buffer,
            buffers,
            offset,
            stream,
            anyOf,
            overlapped,
        }

        //
        // The convention for this enum is using the resource name as the enum name
        //
        internal enum ExceptionResource
        {
            ArgumentOutOfRange_Index,
            ArgumentOutOfRange_IndexCount,
            ArgumentOutOfRange_IndexCountBuffer,
            ArgumentOutOfRange_Count,
            ArgumentOutOfRange_Year,
            Arg_ArrayPlusOffTooSmall,
            NotSupported_ReadOnlyCollection,
            Arg_RankMultiDimNotSupported,
            Arg_NonZeroLowerBound,
            ArgumentOutOfRange_GetCharCountOverflow,
            ArgumentOutOfRange_ListInsert,
            ArgumentOutOfRange_NeedNonNegNum,
            ArgumentOutOfRange_SmallCapacity,
            Argument_InvalidOffLen,
            Argument_CannotExtractScalar,
            ArgumentOutOfRange_BiggerThanCollection,
            Serialization_MissingKeys,
            Serialization_NullKey,
            NotSupported_KeyCollectionSet,
            NotSupported_ValueCollectionSet,
            InvalidOperation_NullArray,
            TaskT_TransitionToFinal_AlreadyCompleted,
            TaskCompletionSourceT_TrySetException_NullException,
            TaskCompletionSourceT_TrySetException_NoExceptions,
            NotSupported_StringComparison,
            ConcurrentCollection_SyncRoot_NotSupported,
            Task_MultiTaskContinuation_NullTask,
            InvalidOperation_WrongAsyncResultOrEndCalledMultiple,
            Task_MultiTaskContinuation_EmptyTaskList,
            Task_Start_TaskCompleted,
            Task_Start_Promise,
            Task_Start_ContinuationTask,
            Task_Start_AlreadyStarted,
            Task_RunSynchronously_Continuation,
            Task_RunSynchronously_Promise,
            Task_RunSynchronously_TaskCompleted,
            Task_RunSynchronously_AlreadyStarted,
            AsyncMethodBuilder_InstanceNotInitialized,
            Task_ContinueWith_ESandLR,
            Task_ContinueWith_NotOnAnything,
            Task_InvalidTimerTimeSpan,
            Task_Delay_InvalidMillisecondsDelay,
            Task_Dispose_NotCompleted,
            Task_ThrowIfDisposed,
            Task_WaitMulti_NullTask,
            ArgumentException_OtherNotArrayOfCorrectLength,
            ArgumentNull_Array,
            ArgumentNull_SafeHandle,
            ArgumentOutOfRange_EndIndexStartIndex,
            ArgumentOutOfRange_Enum,
            ArgumentOutOfRange_HugeArrayNotSupported,
            Argument_AddingDuplicate,
            Argument_InvalidArgumentForComparison,
            Arg_LowerBoundsMustMatch,
            Arg_MustBeType,
            Arg_Need1DArray,
            Arg_Need2DArray,
            Arg_Need3DArray,
            Arg_NeedAtLeast1Rank,
            Arg_RankIndices,
            Arg_RanksAndBounds,
            InvalidOperation_IComparerFailed,
            NotSupported_FixedSizeCollection,
            Rank_MultiDimNotSupported,
            Arg_TypeNotSupported,
            Argument_SpansMustHaveSameLength,
            Argument_InvalidFlag,
            CancellationTokenSource_Disposed,
            Argument_AlignmentMustBePow2,
        }
    }

    internal static partial class SR
    {
        private static readonly bool s_usingResourceKeys = AppContext.TryGetSwitch("System.Resources.UseSystemResourceKeys", out bool usingResourceKeys) ? usingResourceKeys : false;

        // This method is used to decide if we need to append the exception message parameters to the message when calling SR.Format.
        // by default it returns the value of System.Resources.UseSystemResourceKeys AppContext switch or false if not specified.
        // Native code generators can replace the value this returns based on user input at the time of native code generation.
        // The Linker is also capable of replacing the value of this method when the application is being trimmed.
        private static bool UsingResourceKeys() => s_usingResourceKeys;

        internal static string GetResourceString(string resourceKey)
        {
            if (UsingResourceKeys())
            {
                return resourceKey;
            }

            string? resourceString = "";  // We do not have any real resource, so we set it to "" rather than `null` in dotnet/runtime.
            try
            {
                resourceString = ResourceManager.GetString(resourceKey);
#if SYSTEM_PRIVATE_CORELIB || CORERT
                // InternalGetResourceString(resourceKey);
#else
                // ResourceManager.GetString(resourceKey);
#endif
            }
            catch (MissingManifestResourceException) { }

            return resourceString!; // only null if missing resources
        }

        internal static string GetResourceString(string resourceKey, string defaultString)
        {
            string resourceString = GetResourceString(resourceKey);

            return resourceKey == resourceString || resourceString == null ? defaultString : resourceString;
        }

        internal static string Format(string resourceFormat, object? p1)
        {
            if (UsingResourceKeys())
            {
                return string.Join(", ", resourceFormat, p1);
            }

            return string.Format(resourceFormat, p1);
        }

        internal static string Format(string resourceFormat, object? p1, object? p2)
        {
            if (UsingResourceKeys())
            {
                return string.Join(", ", resourceFormat, p1, p2);
            }

            return string.Format(resourceFormat, p1, p2);
        }

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3)
        {
            if (UsingResourceKeys())
            {
                return string.Join(", ", resourceFormat, p1, p2, p3);
            }

            return string.Format(resourceFormat, p1, p2, p3);
        }

        internal static string Format(string resourceFormat, params object?[]? args)
        {
            if (args != null)
            {
                if (UsingResourceKeys())
                {
                    return resourceFormat + ", " + string.Join(", ", args);
                }

                return string.Format(resourceFormat, args);
            }

            return resourceFormat;
        }

        internal static string Format(IFormatProvider? provider, string resourceFormat, object? p1)
        {
            if (UsingResourceKeys())
            {
                return string.Join(", ", resourceFormat, p1);
            }

            return string.Format(provider, resourceFormat, p1);
        }

        internal static string Format(IFormatProvider? provider, string resourceFormat, object? p1, object? p2)
        {
            if (UsingResourceKeys())
            {
                return string.Join(", ", resourceFormat, p1, p2);
            }

            return string.Format(provider, resourceFormat, p1, p2);
        }

        internal static string Format(IFormatProvider? provider, string resourceFormat, object? p1, object? p2, object? p3)
        {
            if (UsingResourceKeys())
            {
                return string.Join(", ", resourceFormat, p1, p2, p3);
            }

            return string.Format(provider, resourceFormat, p1, p2, p3);
        }

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args)
        {
            if (args != null)
            {
                if (UsingResourceKeys())
                {
                    return resourceFormat + ", " + string.Join(", ", args);
                }

                return string.Format(provider, resourceFormat, args);
            }

            return resourceFormat;
        }
    }

    namespace System.Private.CoreLib
    {
        internal static class Strings { }
    }

    internal static partial class SR
    {
        private static global::System.Resources.ResourceManager s_resourceManager;
        internal static global::System.Resources.ResourceManager ResourceManager => s_resourceManager ?? (s_resourceManager = new global::System.Resources.ResourceManager(typeof(System.Private.CoreLib.Strings)));

        /// <summary>The given key '{0}' was not present in the dictionary.</summary>
        internal static string @Arg_KeyNotFoundWithKey => GetResourceString("Arg_KeyNotFoundWithKey");
        internal static string @InvalidOperation_EnumFailedVersion => GetResourceString("InvalidOperation_EnumFailedVersion");
        /// <summary>Enumeration has either not started or has already finished.</summary>
        internal static string @InvalidOperation_EnumOpCantHappen => GetResourceString("InvalidOperation_EnumOpCantHappen");
        /// <summary>The value "{0}" is not of type "{1}" and cannot be used in this generic collection.</summary>
        internal static string @Arg_WrongType => GetResourceString("Arg_WrongType");
        /// <summary>An item with the same key has already been added. Key: {0}</summary>
        internal static string @Argument_AddingDuplicateWithKey => GetResourceString("Argument_AddingDuplicateWithKey");
        /// <summary>Target array type is not compatible with the type of items in the collection.</summary>
        internal static string @Argument_InvalidArrayType => GetResourceString("Argument_InvalidArrayType");
        /// <summary>Mutating a key collection derived from a dictionary is not allowed.</summary>
        internal static string @NotSupported_KeyCollectionSet => GetResourceString("NotSupported_KeyCollectionSet");
        /// <summary>Mutating a value collection derived from a dictionary is not allowed.</summary>
        internal static string @NotSupported_ValueCollectionSet => GetResourceString("NotSupported_ValueCollectionSet");
        /// <summary>The lower bound of target array must be zero.</summary>
        internal static string @Arg_NonZeroLowerBound => GetResourceString("Arg_NonZeroLowerBound");
        /// <summary>Only single dimensional arrays are supported for the requested action.</summary>
        internal static string @Arg_RankMultiDimNotSupported => GetResourceString("Arg_RankMultiDimNotSupported");
        /// <summary>Destination array is not long enough to copy all the items in the collection. Check array index and length.</summary>
        internal static string @Arg_ArrayPlusOffTooSmall => GetResourceString("Arg_ArrayPlusOffTooSmall");
        /// <summary>The Keys for this Hashtable are missing.</summary>
        internal static string @Serialization_MissingKeys => GetResourceString("Serialization_MissingKeys");
        /// <summary>One of the serialized keys is null.</summary>
        internal static string @Serialization_NullKey => GetResourceString("Serialization_NullKey");
        /// <summary>Non-negative number required.</summary>
        internal static string @ArgumentOutOfRange_NeedNonNegNum => GetResourceString("ArgumentOutOfRange_NeedNonNegNum");
    }
}