using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls.Primitives;

namespace Savepoint.App.Features.Common.Accessibility;

/// <summary>
/// A toggle button that opens and closes a section below it. It looks and acts as a <see cref="ToggleButton"/> and also tells
/// assistive technology whether the section is expanded or collapsed, which a plain toggle's on and off does not say.
/// </summary>
public sealed class DisclosureToggleButton : ToggleButton
{
    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(ToggleButton);

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new DisclosurePeer(this);

    private sealed class DisclosurePeer : ToggleButtonAutomationPeer, IExpandCollapseProvider
    {
        private readonly DisclosureToggleButton _owner;

        public DisclosurePeer(DisclosureToggleButton owner)
            : base(owner)
        {
            _owner = owner;
            owner.IsCheckedChanged += (_, _) => RaisePropertyChangedEvent(
                ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                _owner.IsChecked == true ? ExpandCollapseState.Collapsed : ExpandCollapseState.Expanded,
                ExpandCollapseState);
        }

        public ExpandCollapseState ExpandCollapseState => _owner.IsChecked == true ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

        public bool ShowsMenu => false;

        public void Expand() => _owner.IsChecked = true;

        public void Collapse() => _owner.IsChecked = false;
    }
}
