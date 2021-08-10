using System;

namespace StudioCommunication {
    public class HighPriorityAttribute : Attribute { }

    public enum MessageIDs : byte {
        //Connection
        /// <summary>
        /// Unused
        /// </summary>
        Default = 0x00,

        /// <summary>
        /// Structure: GameDataTypes
        /// </summary>
        [HighPriority] GetData = 0x08,

        /// <summary>
        /// Structure:
        /// </summary>
        [HighPriority] EstablishConnection = 0x0D,

        /// <summary>
        /// Structure:
        /// </summary>
        [HighPriority] Wait = 0x0E,

        /// <summary>
        /// Structure:
        /// </summary>
        Reset = 0x0F,

        //Pure data transfer
        /// <summary>
        /// Structure: string[] = { state, gameData }
        /// </summary>
        SendState = 0x10,

        //Data transfer from Studio
        /// <summary>
        /// Structure: string
        /// </summary>
        [HighPriority] SendPath = 0x20,

        /// <summary>
        /// Structure: HotkeyIDs, bool released
        /// </summary>
        [HighPriority] SendHotkeyPressed = 0x21,

        /// <summary>
        /// Structure:
        /// </summary>
        [HighPriority] ConvertToLibTas = 0x24,

        /// <summary>
        /// Structure: string settingName
        /// </summary>
        [HighPriority] ToggleGameSetting = 0x25,

        //Data transfer from CelesteTAS
        /// <summary>
        /// Structure: List&lt;Keys&gt;[];
        /// </summary>
        [HighPriority] SendCurrentBindings = 0x30,

        /// <summary>
        /// Structure: string
        /// </summary>
        [HighPriority] ReturnData = 0x31,

        //External data transfer
        ExternLow1 = 0x40,
        ExternLow2 = 0x41,
        [HighPriority] ExternHigh1 = 0x42,
        [HighPriority] ExternHigh2 = 0x43,
    }

    public enum GameDataTypes : byte {
        ConsoleCommand,
        ModInfo
    }
}