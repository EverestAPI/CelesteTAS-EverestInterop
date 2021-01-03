using System;
using CelesteStudio.TtilebarButton.Utils;

namespace CelesteStudio.TtilebarButton
{
    /// <summary>
    ///     The implementation of ActiveItems used to store a list of buttons in
    ///     the ActiveMenu.
    /// </summary>
    internal class ActiveButtonCollection : ListCollection<IActiveButton>, IActiveButtonCollection
    {
        private ActiveMenuForm Owner { get; }

        public ActiveButtonCollection(ActiveMenuForm owner)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            Owner = owner;
        }


        public IActiveButton CreateItem()
        {
            return new ActiveButton();
        }
        public IActiveButton CreateItem(string text, EventHandler click)
        {
            var button = new ActiveButton();
            button.Text = text;
            button.Click += click;

            return button;
        }
        public IActiveButton Add(string text, EventHandler click)
        {
            var button = CreateItem(text, click);
            Add(button);
            return button;
        }

        protected override void OnListChanged()
        {
            Owner.OnItemsCollectionChanged();
        }
    }
}
