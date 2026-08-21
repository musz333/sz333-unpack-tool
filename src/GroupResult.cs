namespace DSUnpack
{
public class GroupResult
{
	public bool Success;

	/// <summary>用户请求取消（与失败区分：不弹失败提示、不删除任何原始文件）</summary>
	public bool Cancelled;

	public string BaseName;

	public string FinalPath;

	public string Error;

	public int NestLevels;

	/// <summary>本次解压各层实际命中的密码（用于保存工作流）</summary>
	public System.Collections.Generic.List<string> UsedPasswords;

	/// <summary>本次解压链路步骤（外层 → 内层，用于保存工作流）</summary>
	public System.Collections.Generic.List<WorkflowStep> Steps;
}

}
