namespace HLSLInterpreter.Debugger.State;

public partial class AppStore
{
    public void OpenModal(ModalKind modal) => UpdateUi(u => u with { OpenModal = modal });

    public void CloseModal() => UpdateUi(u => u with { OpenModal = ModalKind.None });

    public void SetMenuOpen(bool open) => UpdateUi(u => u with { MenuOpen = open });

    public void SetBonzomaticMode(bool enabled) => UpdateUi(u => u with { BonzomaticMode = enabled });

    public void SetImageCollapsed(bool collapsed) => UpdateUi(u => u with { ImageCollapsed = collapsed });

    private void UpdateUi(Func<UiState, UiState> update) =>
        Update(s => s with { Ui = update(s.Ui) });
}
