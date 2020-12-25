namespace CelesteStudio.TtilebarButton
{
    /// <summary>
    ///     Provides access to the <see cref="T:ActiveButton"></see> instances
    ///     attached to the menu instance.
    /// </summary>
    /// <example>
    ///     This sample shows how to add &amp; remove buttons form the IActiveMenu
    ///     using the Items list.
    ///     <code>
    ///     // get an instance of the menu for the current form
    ///     IActiveMenu menu = ActiveMenu.GetInstance(this);
    /// 
    ///     // add button to front the menu
    ///     menu.Items.Add(button);
    /// 
    ///     // insert button at position 2
    ///     menu.Items.Insert(2, button);
    /// 
    ///     // remove specific button
    ///     menu.Remove(button);
    /// 
    ///     // remove button at position 2
    ///     menu.RemoveAt(2);
    /// </code>
    /// </example>
    public interface IActiveMenu
    {
        /// <summary>
        ///     Gets the list of <see cref="T:ActiveButton"></see> instances
        ///     associated with the current menu instances.
        /// </summary>
        /// <value>The items.</value>
        IActiveButtonCollection Items { get; }


        void SuspendLayout();
        void ResumeLayout();
    }
}
