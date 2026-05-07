public interface IGameMode
{
    string DisplayName { get; }
    PackedScene Level { get; }
    void LoadLevel();
    void GetRelevantStats();
}
