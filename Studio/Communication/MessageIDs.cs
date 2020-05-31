using System;

namespace CelesteStudio.Communication {
	public class HighPriorityAttribute : Attribute { }

	public enum MessageIDs : byte {
		//Connection
		/// <summary>
		/// Unused
		/// </summary>
		Default = 0x00,
		[HighPriority]
		/// <summary>
		/// Structure:
		/// </summary>
		EstablishConnection = 0x0D,
		[HighPriority]
		/// <summary>
		/// Structure:
		/// </summary>
		Wait = 0x0E,
		/// <summary>
		/// Structure:
		/// </summary>
		Reset = 0x0F,

		//Pure data transfer
		/// <summary>
		/// Structure: string[] = { state, playerData }
		/// </summary>
		SendState = 0x10,
		/// <summary>
		/// Structure: string
		/// </summary>
		SendPlayerData = 0x11,

		//Data transfer from Studio
		[HighPriority]
		/// <summary>
		/// Structure: string
		/// </summary>
		SendPath = 0x20,
		[HighPriority]
		/// <summary>
		/// Structure: HotkeyIDs
		/// </summary>
		SendHotkeyPressed = 0x21,
		[HighPriority]
		/// <summary>
		/// Structure: HotkeyIDs List&lt;Keys&gt;
		/// </summary>
		SendNewBindings = 0x22,
		[HighPriority]
		/// <summary>
		/// Structure: 
		/// </summary>
		ReloadBindings = 0x23,

		//Data transfer from CelesteTAS
		[HighPriority]
		/// <summary>
		/// Structure: List&lt;Keys&gt;[];
		/// </summary>
		SendCurrentBindings = 0x30,

	}
}
