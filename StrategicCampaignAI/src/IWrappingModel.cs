namespace StrategicCampaignAI;

/// <summary>A model of ours that wraps the one registered before it; names it for the startup log.</summary>
internal interface IWrappingModel
{
    string WrappedModelName { get; }
}
