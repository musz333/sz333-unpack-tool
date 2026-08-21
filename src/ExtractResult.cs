namespace DSUnpack
{
public class ExtractResult
{
	public bool Ok;

	public bool PasswordError;

	/// <summary>用户请求取消（进程被中断），与普通失败区分</summary>
	public bool Cancelled;

	public string Error;

	public string UsedPassword;
}

}
