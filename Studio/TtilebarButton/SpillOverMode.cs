namespace CelesteStudio.TtilebarButton
{
    /// <summary>
    ///     Determines the behaviour of the <see cref="T:ActiveButton"></see>
    ///     items when they spill over the edge of the title bar.
    /// </summary>
    internal enum SpillOverMode
    {
        /// <summary>
        ///     Hide <see cref="T:ActiveButton"></see> instances.
        /// </summary>
        Hide,
        /// <summary>
        ///     Increase the minimum size of the parent form to compensate.
        /// </summary>
        IncreaseSize
    }
}
