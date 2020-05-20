namespace TAS.StudioCommunication {
	public enum MessageIDs : byte {
		//Connection
		/// <summary>
		/// Unused
		/// </summary>
		Default = 0x00,
		/// <summary>
		/// Structure: ID Length
		/// </summary>
		EstablishConnection = 0x0D,
		/// <summary>
		/// Structure: ID Length PreviousID
		/// </summary>
		Confirm = 0x0E,
		/// <summary>
		/// Unused
		/// </summary>
		Respond = 0x0F,

		//Pure data transfer
		/// <summary>
		/// Structure: ID Length string
		/// </summary>
		SendState = 0x10,
		/// <summary>
		/// Structure: ID Length string
		/// </summary>
		SendPlayerData = 0x11,

		//Data transfer from Studio
		/// <summary>
		/// Structure: ID Length string
		/// </summary>
		SendPath = 0x20,
		/// <summary>
		/// Structure: ID Length HotkeyIDs
		/// </summary>
		SendHotkeyPressed = 0x21,
		/// <summary>
		/// Structure: ID Length HotkeyIDs List&lt;Keys&gt;
		/// </summary>
		SendNewBindings = 0x22,
		/// <summary>
		/// Structure: ID Length
		/// </summary>
		ReloadBindings = 0x23,

		//Data transfer from CelesteTAS
		/// <summary>
		/// Structure: ID Length List&lt;Keys&gt;[];
		/// </summary>
		SendCurrentBindings = 0x30,

	}
}
