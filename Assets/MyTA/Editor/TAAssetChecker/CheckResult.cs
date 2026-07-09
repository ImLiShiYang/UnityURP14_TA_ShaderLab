public class CheckResult
{
    public string assetPath;
    public string assetType;

    public string ruleName;
    public string currentValue;
    public string limitValue;
    public string message;

    // 用来显示更详细的信息，比如模型里每个 Mesh 的顶点数明细。
    public string detailMessage;

    public bool passed;
    public bool canFix;

    public AssetCheckRule rule;
}