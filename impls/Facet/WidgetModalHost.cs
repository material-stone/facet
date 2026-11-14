using System.Diagnostics;
using System.Windows.Input;
using Facet.Plugins;

namespace Facet;

// Shows one widget-owned view over the whole overlay. Only the presentation lives here; the content belongs to the widget.
internal sealed class WidgetModalHost(Grid layer, Border frame)
{
    private sealed class Session(WidgetModalHost host, string owner, ModalOptions options, IInputElement? returnFocus) : IModalHandle
    {
        internal string Owner => owner;
        internal ModalOptions Options => options;
        internal IInputElement? ReturnFocus => returnFocus;
        public bool IsOpen => ReferenceEquals(host.current, this);
        public event Action? Closed;
        public void Close() { if (IsOpen) host.Finish(); }
        internal void RaiseClosed() { var closed = Closed; Closed = null; closed?.Invoke(); }
    }

    private sealed class Scope(WidgetModalHost host, string owner) : IWidgetModal
    {
        public IModalHandle? Show(FrameworkElement content, ModalOptions? options = null) => host.Show(owner, content, options);
    }

    private Session? current;
    internal bool IsOpen => current != null;
    internal IWidgetModal For(string owner) => new Scope(this, owner);

    private Session? Show(string owner, FrameworkElement content, ModalOptions? options)
    {
        if (current != null || content == null || content.Parent != null) return null;
        current = new Session(this, owner, options ?? new(), Keyboard.FocusedElement);
        frame.Child = content;
        layer.Visibility = Visibility.Visible;
        var session = current;
        layer.Dispatcher.BeginInvoke(new Action(() => { if (session.IsOpen && layer.IsVisible) frame.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)); }));
        return session;
    }

    private static bool Ask(Func<bool>? question, bool fallback)
    {
        if (question == null) return fallback;
        // Plugin code can throw anything; a failing question must not trap the user in the modal.
        try { return question(); }
        catch (Exception ex) { Trace.TraceWarning("Widget modal callback failed: " + ex.Message); return fallback; }
    }

    // Escape, an outside click and hiding end here. Returns false when the content keeps the modal open.
    internal bool TryClose()
    {
        if (current == null) return true;
        if (!Ask(current.Options.CanClose, true)) return false;
        Finish();
        return true;
    }

    internal void HandleEscape()
    {
        if (current != null && !Ask(current.Options.HandleEscape, false)) TryClose();
    }

    internal void CloseOwned(string owner) { if (current?.Owner == owner) Finish(); }
    internal void CloseAll() { if (current != null) Finish(); }

    private void Finish()
    {
        var session = current!;
        current = null;
        frame.Child = null;
        layer.Visibility = Visibility.Collapsed;
        try { session.RaiseClosed(); }
        catch (Exception ex) { Trace.TraceWarning("Widget modal Closed failed: " + ex.Message); }
        if (session.ReturnFocus is UIElement { IsVisible: true } target) target.Focus();
    }
}
