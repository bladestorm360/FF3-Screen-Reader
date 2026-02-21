using System;

namespace FFIII_ScreenReader.Utils
{
    /// <summary>
    /// Helper for reading state machine values from IL2CPP controller objects.
    /// Consolidates the unsafe pointer arithmetic used across multiple *Patches files.
    /// </summary>
    internal static class StateReaderHelper
    {

        /// <summary>
        /// Reads the state tag from a controller's state machine using unsafe pointer arithmetic.
        /// </summary>
        /// <param name="controllerPtr">Pointer to the controller instance</param>
        /// <param name="stateMachineOffset">Offset to the stateMachine field (varies by controller type)</param>
        /// <returns>State tag value, or -1 if read failed</returns>
        /// <remarks>
        /// Known offsets by controller type:
        /// - BattleCommandSelectController: 0x48
        /// - EquipmentWindowController: 0x60
        /// - ItemWindowController: 0x70
        /// - AbilityWindowController: 0x88
        /// - ShopController: 0x98
        /// </remarks>
        public static int ReadStateTag(IntPtr controllerPtr, int stateMachineOffset)
        {
            if (controllerPtr == IntPtr.Zero)
                return -1;

            try
            {
                unsafe
                {
                    // Read stateMachine pointer at the specified offset
                    IntPtr stateMachinePtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + stateMachineOffset);
                    if (stateMachinePtr == IntPtr.Zero)
                        return -1;

                    // Read current State<T> pointer at offset 0x10
                    IntPtr currentStatePtr = *(IntPtr*)((byte*)stateMachinePtr.ToPointer() + IL2CppOffsets.StateMachine.OFFSET_CURRENT);
                    if (currentStatePtr == IntPtr.Zero)
                        return -1;

                    // Read Tag (int) at offset 0x10
                    int stateTag = *(int*)((byte*)currentStatePtr.ToPointer() + IL2CppOffsets.StateMachine.OFFSET_TAG);
                    return stateTag;
                }
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// Convenience method using IL2CPP object's Pointer property.
        /// </summary>
        /// <param name="controller">The IL2CPP controller object</param>
        /// <param name="stateMachineOffset">Offset to the stateMachine field</param>
        /// <returns>State tag value, or -1 if read failed</returns>
        public static int ReadStateTag(Il2CppSystem.Object controller, int stateMachineOffset)
        {
            if (controller == null)
                return -1;
            return ReadStateTag(controller.Pointer, stateMachineOffset);
        }

        /// <summary>
        /// Reads a pointer value at the specified offset from an object.
        /// Useful for reading other fields like targetCharacter.
        /// </summary>
        /// <param name="objectPtr">Pointer to the object</param>
        /// <param name="offset">Offset to the pointer field</param>
        /// <returns>The pointer value, or IntPtr.Zero if read failed</returns>
        public static IntPtr ReadPointerField(IntPtr objectPtr, int offset)
        {
            if (objectPtr == IntPtr.Zero)
                return IntPtr.Zero;

            try
            {
                unsafe
                {
                    return *(IntPtr*)((byte*)objectPtr.ToPointer() + offset);
                }
            }
            catch
            {
                return IntPtr.Zero;
            }
        }
    }
}
