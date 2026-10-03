using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: CompilationRelaxations(8)]
[assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]
[assembly: Debuggable(DebuggableAttribute.DebuggingModes.IgnoreSymbolStoreSequencePoints)]
[assembly: TargetFramework(".NETCoreApp,Version=v8.0", FrameworkDisplayName = ".NET 8.0")]
[assembly: AssemblyCompany("BO2RTM")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyFileVersion("6.32.0.0")]
[assembly: AssemblyInformationalVersion("6.32.0")]
[assembly: AssemblyProduct("BO2RTM")]
[assembly: AssemblyTitle("BO2RTM")]
[assembly: AssemblyMetadata("BO2RTM.UpdateManifestUrl", "")]
[assembly: TargetPlatform("Windows7.0")]
[assembly: SupportedOSPlatform("Windows7.0")]
[assembly: AssemblyVersion("6.32.0.0")]
[module: RefSafetyRules(11)]
[CompilerGenerated]
internal static class ApplicationConfiguration
{
	public static void Initialize()
	{
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		Application.SetHighDpiMode(HighDpiMode.SystemAware);
	}
}
namespace BO2RTM;

internal sealed class DebugBridge
{
	private sealed record ProcessCandidate(object? Pid, string Name);

	public sealed class PlayerRead80Result
	{
		public List<string> Names { get; } = new List<string>();

		public List<string> Diagnostics { get; } = new List<string>();

		public int Active { get; set; }
	}

	public sealed record PlayerWindowRead(ulong Address, byte[] Bytes);

	public sealed record RegionSnapshot(ulong Address, byte[] Bytes);

	private readonly string ip;

	private readonly Assembly asm;

	private readonly Type apiType;

	private readonly object api;

	private int cbufRpcPid = -1;

	private ulong cbufRpcStub;

	public const ulong LobbyNameTableBase = 21586424uL;

	public const ulong LobbyNameStride = 328uL;

	public const int LobbyNameSlots = 18;

	public const int LobbyNameMaxLen = 32;

	private readonly Dictionary<string, Dictionary<int, byte[]>> linkedRecordWatchStates = new Dictionary<string, Dictionary<int, byte[]>>(StringComparer.OrdinalIgnoreCase);

	public void InvalidateCbufRpcSession()
	{
		cbufRpcPid = -1;
		cbufRpcStub = 0uL;
	}

	public ulong PrepareCbufRpcSession(object process)
	{
		int num = ExtractPid(process);
		if (cbufRpcPid == num && cbufRpcStub != 0L)
		{
			return cbufRpcStub;
		}
		InvalidateCbufRpcSession();
		ulong num2 = InvokeInstallRpc(num);
		if (num2 == 0L)
		{
			throw new InvalidOperationException("InstallRPC returned 0 while preparing the Cbuf RPC session.");
		}
		cbufRpcPid = num;
		cbufRpcStub = num2;
		return num2;
	}

	public DebugBridge(string ipAddress)
	{
		ip = ipAddress;
		Assembly assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault((Assembly a) => string.Equals(a.GetName().Name, "libdebug", StringComparison.OrdinalIgnoreCase));
		if (assembly != null)
		{
			asm = assembly;
		}
		else
		{
			using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("r_8F4A2C91") ?? throw new FileNotFoundException("Embedded PS4 debug library resource 'r_8F4A2C91' is missing.");
			using MemoryStream memoryStream = new MemoryStream();
			stream.CopyTo(memoryStream);
			asm = Assembly.Load(memoryStream.ToArray());
		}
		apiType = asm.GetTypes().FirstOrDefault((Type t) => t.Name.Equals("PS4DBG", StringComparison.OrdinalIgnoreCase)) ?? asm.GetTypes().FirstOrDefault((Type t) => t.GetMethods().Any((MethodInfo m) => m.Name == "GetProcessList")) ?? throw new InvalidOperationException("Could not locate the PS4 debug API type in libdebug.dll.");
		api = CreateApi(apiType);
	}

	private object CreateApi(Type t)
	{
		foreach (ConstructorInfo item in from x in t.GetConstructors()
			orderby x.GetParameters().Length
			select x)
		{
			ParameterInfo[] parameters = item.GetParameters();
			try
			{
				if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
				{
					return item.Invoke(new object[1] { ip });
				}
				if (parameters.Length == 0)
				{
					object obj = item.Invoke(null);
					SetStringProperty(obj, "IP", ip);
					SetStringProperty(obj, "Ip", ip);
					SetStringProperty(obj, "IPAddress", ip);
					return obj;
				}
			}
			catch
			{
			}
		}
		throw new InvalidOperationException("No supported constructor found for " + t.FullName + ".");
	}

	private static void SetStringProperty(object o, string name, string value)
	{
		PropertyInfo property = o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
		if ((object)property != null && property.CanWrite && property.PropertyType == typeof(string))
		{
			property.SetValue(o, value);
		}
	}

	public void Connect()
	{
		MethodInfo[] array = (from m in apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
			where m.Name == "Connect"
			select m).ToArray();
		if (array.Length == 0)
		{
			return;
		}
		foreach (MethodInfo item in array.OrderBy((MethodInfo x) => x.GetParameters().Length))
		{
			ParameterInfo[] parameters = item.GetParameters();
			try
			{
				if (parameters.Length == 0)
				{
					item.Invoke(api, null);
					break;
				}
				if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
				{
					item.Invoke(api, new object[1] { ip });
					break;
				}
			}
			catch (TargetInvocationException ex)
			{
				throw ex.InnerException ?? ex;
			}
		}
	}

	public void NotifyConsole(int messageType, string message)
	{
		if (string.IsNullOrWhiteSpace(message))
		{
			throw new ArgumentException("Notification text is empty.", "message");
		}
		MethodInfo methodInfo = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault(delegate(MethodInfo m)
		{
			ParameterInfo[] parameters = m.GetParameters();
			return m.Name == "Notify" && parameters.Length == 2 && parameters[0].ParameterType == typeof(int) && parameters[1].ParameterType == typeof(string);
		}) ?? throw new MissingMethodException(apiType.FullName, "Notify(int messageType, string message)");
		try
		{
			methodInfo.Invoke(api, new object[2] { messageType, message });
		}
		catch (TargetInvocationException ex)
		{
			throw ex.InnerException ?? ex;
		}
	}

	public void Disconnect()
	{
		MethodInfo methodInfo = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo m) => m.Name == "Disconnect" && m.GetParameters().Length == 0);
		if (methodInfo == null)
		{
			return;
		}
		try
		{
			methodInfo.Invoke(api, null);
		}
		catch (TargetInvocationException ex)
		{
			throw ex.InnerException ?? ex;
		}
	}

	public ProcessSearchResult FindBO2Process(bool zombies = false)
	{
		List<string> list = new List<string>();
		list.Add("libdebug API: " + apiType.FullName);
		MethodInfo methodInfo = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "GetProcessList" && x.GetParameters().Length == 0) ?? throw new MissingMethodException(apiType.FullName, "GetProcessList()");
		object obj;
		try
		{
			obj = methodInfo.Invoke(api, null);
		}
		catch (TargetInvocationException ex)
		{
			throw ex.InnerException ?? ex;
		}
		if (obj == null)
		{
			list.Add("GetProcessList returned null.");
			return new ProcessSearchResult(null, "Not found", list);
		}
		List<object> list2 = Flatten(obj).ToList();
		list.Add($"Process entries returned: {list2.Count}");
		object obj2 = null;
		string text = "";
		foreach (object item in list2)
		{
			string text2 = Describe(item);
			list.Add("Process: " + text2);
			string text3 = text2.ToLowerInvariant();
			if (obj2 == null && (zombies ? text3.Contains("codzm.elf") : text3.Contains("codmp.elf")))
			{
				obj2 = item;
				text = text2;
			}
		}
		return new ProcessSearchResult(obj2, (obj2 == null) ? "Not found" : text, list);
	}

	private static IEnumerable<object> Flatten(object raw)
	{
		Type type = raw.GetType();
		if (type.GetField("processes", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(raw) is IEnumerable enumerable)
		{
			foreach (object item in enumerable)
			{
				if (item != null)
				{
					yield return item;
				}
			}
			yield break;
		}
		object obj = GetMemberValue(raw, "pids") ?? GetMemberValue(raw, "Pids") ?? GetMemberValue(raw, "PIDs");
		object obj2 = GetMemberValue(raw, "names") ?? GetMemberValue(raw, "Names");
		if (obj is IEnumerable source && obj2 is IEnumerable source2 && !(obj is string) && !(obj2 is string))
		{
			List<object> pa = source.Cast<object>().ToList();
			List<object> na = source2.Cast<object>().ToList();
			for (int i = 0; i < Math.Min(pa.Count, na.Count); i++)
			{
				if (pa[i] != null || na[i] != null)
				{
					yield return new ProcessCandidate(pa[i], na[i]?.ToString() ?? "<unnamed>");
				}
			}
			yield break;
		}
		if (raw is string)
		{
			yield return raw;
			yield break;
		}
		if (raw is IEnumerable enumerable2)
		{
			foreach (object item2 in enumerable2)
			{
				if (item2 != null)
				{
					yield return item2;
				}
			}
			yield break;
		}
		PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
		foreach (PropertyInfo propertyInfo in properties)
		{
			object obj3 = null;
			try
			{
				obj3 = propertyInfo.GetValue(raw);
			}
			catch
			{
			}
			if (!(obj3 is IEnumerable enumerable3) || obj3 is string)
			{
				continue;
			}
			foreach (object item3 in enumerable3)
			{
				if (item3 != null)
				{
					yield return item3;
				}
			}
			yield break;
		}
		FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
		foreach (FieldInfo fieldInfo in fields)
		{
			object obj5 = null;
			try
			{
				obj5 = fieldInfo.GetValue(raw);
			}
			catch
			{
			}
			if (!(obj5 is IEnumerable enumerable4) || obj5 is string)
			{
				continue;
			}
			foreach (object item4 in enumerable4)
			{
				if (item4 != null)
				{
					yield return item4;
				}
			}
			yield break;
		}
		yield return raw;
	}

	private static object? GetMemberValue(object o, string name)
	{
		Type type = o.GetType();
		PropertyInfo property = type.GetProperty(name, BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public);
		if (property != null && property.GetIndexParameters().Length == 0)
		{
			try
			{
				return property.GetValue(o);
			}
			catch
			{
			}
		}
		FieldInfo field = type.GetField(name, BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public);
		if (field != null)
		{
			try
			{
				return field.GetValue(o);
			}
			catch
			{
			}
		}
		return null;
	}

	private static string Describe(object o)
	{
		if (o is string result)
		{
			return result;
		}
		if (o is ProcessCandidate processCandidate)
		{
			return $"PID={processCandidate.Pid}, Name={processCandidate.Name}";
		}
		List<string> list = new List<string>();
		PropertyInfo[] properties = o.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
		foreach (PropertyInfo propertyInfo in properties)
		{
			if (propertyInfo.GetIndexParameters().Length != 0)
			{
				continue;
			}
			try
			{
				object value = propertyInfo.GetValue(o);
				if (value != null && (value is string || value.GetType().IsPrimitive || value is nint || value is nuint))
				{
					list.Add($"{propertyInfo.Name}={value}");
				}
			}
			catch
			{
			}
		}
		FieldInfo[] fields = o.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (FieldInfo fieldInfo in fields)
		{
			try
			{
				object value2 = fieldInfo.GetValue(o);
				if (value2 != null && (value2 is string || value2.GetType().IsPrimitive || value2 is nint || value2 is nuint))
				{
					list.Add($"{fieldInfo.Name}={value2}");
				}
			}
			catch
			{
			}
		}
		string text;
		if (list.Count <= 0)
		{
			text = o.ToString();
			if (text == null)
			{
				return o.GetType().Name;
			}
		}
		else
		{
			text = string.Join(", ", list);
		}
		return text;
	}

	public MemoryReadResult ValidateRead(object process)
	{
		int num = ExtractPid(process);
		MethodInfo methodInfo = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "GetProcessMaps" && x.GetParameters().Length == 1) ?? throw new MissingMethodException(apiType.FullName, "GetProcessMaps(pid)");
		object obj;
		try
		{
			obj = methodInfo.Invoke(api, new object[1] { num });
		}
		catch (TargetInvocationException ex)
		{
			throw ex.InnerException ?? ex;
		}
		if (obj == null)
		{
			throw new InvalidOperationException("GetProcessMaps returned null.");
		}
		IEnumerable source = ((GetMemberValue(obj, "entries") ?? GetMemberValue(obj, "Entries")) as IEnumerable) ?? throw new InvalidOperationException("Process map entries were not enumerable.");
		MethodInfo methodInfo2 = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		List<string> list = new List<string>();
		foreach (object item in from object x in source
			where x != null
			select x)
		{
			ulong num2 = ToUInt64(GetMemberValue(item, "start") ?? GetMemberValue(item, "Start"));
			ulong num3 = ToUInt64(GetMemberValue(item, "end") ?? GetMemberValue(item, "End"));
			if (num2 == 0L || num3 <= num2)
			{
				continue;
			}
			int num4 = (int)Math.Min(32uL, num3 - num2);
			if (num4 <= 0)
			{
				continue;
			}
			try
			{
				if (methodInfo2.Invoke(api, new object[3] { num, num2, num4 }) is byte[] array && array.Length != 0)
				{
					string preview = BitConverter.ToString(array.Take(16).ToArray()).Replace("-", " ");
					return new MemoryReadResult(num, num2, num4, array.Length, preview);
				}
			}
			catch (TargetInvocationException ex2)
			{
				if (list.Count < 3)
				{
					list.Add(ex2.InnerException?.Message ?? ex2.Message);
				}
			}
		}
		throw new InvalidOperationException("No readable process map was found." + ((list.Count > 0) ? (" First errors: " + string.Join(" | ", list)) : ""));
	}

	private static int ExtractPid(object process)
	{
		object? obj = GetMemberValue(process, "pid") ?? GetMemberValue(process, "Pid") ?? GetMemberValue(process, "PID");
		if (obj == null)
		{
			throw new InvalidOperationException("Selected process did not expose a PID.");
		}
		return Convert.ToInt32(obj);
	}

	private static ulong ToUInt64(object? v)
	{
		if (v == null)
		{
			return 0uL;
		}
		if (v is nuint num)
		{
			return ((UIntPtr)num).ToUInt64();
		}
		if (v is nint num2)
		{
			return (ulong)((IntPtr)num2).ToInt64();
		}
		return Convert.ToUInt64(v);
	}

	public BO2ValidationResult ValidateBO2Specific(object process)
	{
		int pid = ExtractPid(process);
		MethodInfo methodInfo = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "GetProcessMaps" && x.GetParameters().Length == 1) ?? throw new MissingMethodException(apiType.FullName, "GetProcessMaps(pid)");
		object obj;
		try
		{
			obj = methodInfo.Invoke(api, new object[1] { pid });
		}
		catch (TargetInvocationException ex)
		{
			throw ex.InnerException ?? ex;
		}
		if (obj == null)
		{
			throw new InvalidOperationException("GetProcessMaps returned null.");
		}
		IEnumerable source = ((GetMemberValue(obj, "entries") ?? GetMemberValue(obj, "Entries")) as IEnumerable) ?? throw new InvalidOperationException("Process map entries were not enumerable.");
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		ulong num = 0uL;
		foreach (object item in from object x in source
			where x != null
			select x)
		{
			ulong num2 = ToUInt64(GetMemberValue(item, "start") ?? GetMemberValue(item, "Start"));
			ulong num3 = ToUInt64(GetMemberValue(item, "end") ?? GetMemberValue(item, "End"));
			uint num4 = Convert.ToUInt32(GetMemberValue(item, "prot") ?? GetMemberValue(item, "Prot") ?? ((object)0u));
			if (num2 == 0L || num3 <= num2 || (num4 & 5) != 5)
			{
				continue;
			}
			ulong num5 = num3 - num2;
			byte[] array = Array.Empty<byte>();
			for (ulong num6 = 0uL; num6 + 32 < num5; num6 += 262016)
			{
				if (num != 0L)
				{
					break;
				}
				int num7 = (int)Math.Min(262144uL, num5 - num6);
				byte[] array2;
				try
				{
					array2 = Read(num2 + num6, num7);
				}
				catch
				{
					break;
				}
				if (array2.Length != num7)
				{
					break;
				}
				byte[] array3 = new byte[array.Length + array2.Length];
				Buffer.BlockCopy(array, 0, array3, 0, array.Length);
				Buffer.BlockCopy(array2, 0, array3, array.Length, array2.Length);
				ulong num8 = num2 + num6 - (ulong)array.Length;
				for (int num9 = 0; num9 + 5 <= array3.Length; num9++)
				{
					if (num != 0L)
					{
						break;
					}
					if (array3[num9] != 191 || array3[num9 + 1] != 49 || array3[num9 + 2] != 0 || array3[num9 + 3] != 0 || array3[num9 + 4] != 0)
					{
						continue;
					}
					for (int num10 = num9 + 5; num10 < Math.Min(num9 + 69, array3.Length - 5); num10++)
					{
						if (num != 0L)
						{
							break;
						}
						if (array3[num10] != 232)
						{
							continue;
						}
						for (int num11 = num10 + 5; num11 < Math.Min(num10 + 37, array3.Length - 4); num11++)
						{
							if (array3[num11] == 72 && array3[num11 + 1] == 139 && array3[num11 + 2] == 64 && array3[num11 + 3] == 16)
							{
								int num12 = BitConverter.ToInt32(array3, num10 + 1);
								num = (ulong)((long)num8 + (long)num10 + 5 + num12);
								break;
							}
						}
					}
				}
				int num13 = Math.Min(128, array2.Length);
				array = array2.Skip(array2.Length - num13).ToArray();
			}
			if (num != 0L)
			{
				break;
			}
		}
		if (num == 0L)
		{
			throw new InvalidOperationException("BO2 signature scan did not resolve the known DB function.");
		}
		if (num < 1829248)
		{
			throw new InvalidOperationException("Resolved DB address was below the expected BO2 base delta.");
		}
		ulong num14 = num - 1829248;
		byte[] array4 = Read(num14 + 12827128, 8);
		if (array4.Length != 8)
		{
			throw new InvalidOperationException("BO2 client-base pointer validation read was incomplete.");
		}
		ulong clientBasePtrValue = BitConverter.ToUInt64(array4, 0);
		return new BO2ValidationResult(pid, num, num14, num14 + 12827128, clientBasePtrValue);
		byte[] Read(ulong addr, int len)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, addr, len }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex2)
			{
				throw ex2.InnerException ?? ex2;
			}
		}
	}

	public List<BO2ClientRead> ReadBO2Clients(object process)
	{
		BO2ValidationResult bO2ValidationResult = ValidateBO2Specific(process);
		int pid = bO2ValidationResult.Pid;
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		ulong num = bO2ValidationResult.GameBase + 27019072;
		List<ulong> list = new List<ulong> { num };
		if (Ptr(bO2ValidationResult.ClientBasePtrValue) && bO2ValidationResult.ClientBasePtrValue != num)
		{
			list.Add(bO2ValidationResult.ClientBasePtrValue);
		}
		List<BO2ClientRead> list2 = new List<BO2ClientRead>();
		HashSet<ulong> hashSet = new HashSet<ulong>();
		foreach (ulong item in list)
		{
			for (int num2 = 0; num2 < 18; num2++)
			{
				ulong num3 = item + (ulong)((long)num2 * 872L);
				uint num4;
				try
				{
					num4 = BitConverter.ToUInt32(Read(num3 + 32, 4), 0);
				}
				catch
				{
					continue;
				}
				if (num4 == 0)
				{
					continue;
				}
				ulong num5;
				try
				{
					num5 = BitConverter.ToUInt64(Read(num3 + 344, 8), 0);
				}
				catch
				{
					continue;
				}
				if (Ptr(num5) && hashSet.Add(num3))
				{
					uint rank = 0u;
					uint prestige = 0u;
					try
					{
						rank = BitConverter.ToUInt32(Read(num5 + 21848, 4), 0);
					}
					catch
					{
					}
					try
					{
						prestige = BitConverter.ToUInt32(Read(num5 + 21852, 4), 0);
					}
					catch
					{
					}
					string candidateStrings = "";
					try
					{
						candidateStrings = FindPrintableStrings(Read(num3, 872));
					}
					catch
					{
					}
					list2.Add(new BO2ClientRead(num2, num3, num5, num4, rank, prestige, candidateStrings));
				}
			}
		}
		return list2;
		static bool Ptr(ulong p)
		{
			if (p >= 65536)
			{
				return p <= 281474976710655L;
			}
			return false;
		}
		byte[] Read(ulong addr, int len)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, addr, len }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex)
			{
				throw ex.InnerException ?? ex;
			}
		}
	}

	public List<string> DiagnoseBO2ClientSlots(object process)
	{
		BO2ValidationResult bO2ValidationResult = ValidateBO2Specific(process);
		int pid = bO2ValidationResult.Pid;
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		ulong num = bO2ValidationResult.GameBase + 27019072;
		List<(string, ulong)> list = new List<(string, ulong)> { ("static", num) };
		if (Ptr(bO2ValidationResult.ClientBasePtrValue) && bO2ValidationResult.ClientBasePtrValue != num)
		{
			list.Add(("pointer", bO2ValidationResult.ClientBasePtrValue));
		}
		List<string> list2 = new List<string>();
		list2.Add($"Player slot diagnostics: pid={pid}, staticBase=0x{num:X}, pointerValue=0x{bO2ValidationResult.ClientBasePtrValue:X}, stride=0x{872uL:X}");
		foreach (var item in list)
		{
			list2.Add($"Testing {item.Item1} client base 0x{item.Item2:X}");
			for (int num2 = 0; num2 < 18; num2++)
			{
				ulong num3 = item.Item2 + (ulong)((long)num2 * 872L);
				string value;
				try
				{
					byte[] array = Read(num3 + 32, 4);
					value = ((array.Length >= 4) ? $"0x{BitConverter.ToUInt32(array, 0):X8}" : $"short({array.Length}/4)");
				}
				catch (Exception ex)
				{
					value = "FAIL:" + ex.GetBaseException().Message;
				}
				string value2;
				try
				{
					byte[] array2 = Read(num3 + 344, 8);
					value2 = ((array2.Length >= 8) ? $"0x{BitConverter.ToUInt64(array2, 0):X}" : $"short({array2.Length}/8)");
				}
				catch (Exception ex2)
				{
					value2 = "FAIL:" + ex2.GetBaseException().Message;
				}
				string value3;
				try
				{
					byte[] array3 = Read(num3, 64);
					value3 = $"{array3.Length}/64 [{string.Join(" ", from z in array3.Take(16)
						select z.ToString("X2"))}]";
				}
				catch (Exception ex3)
				{
					value3 = "FAIL:" + ex3.GetBaseException().Message;
				}
				list2.Add($"Slot {num2:D2} {item.Item1}: client=0x{num3:X} marker={value} ps={value2} read={value3}");
			}
		}
		list2.Add("Player slot diagnostics complete. No game memory was modified.");
		return list2;
		static bool Ptr(ulong p)
		{
			if (p >= 65536)
			{
				return p <= 281474976710655L;
			}
			return false;
		}
		byte[] Read(ulong addr, int len)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, addr, len }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex4)
			{
				throw ex4.InnerException ?? ex4;
			}
		}
	}

	public List<string> SearchAsciiInBO2(object process, string target)
	{
		if (string.IsNullOrWhiteSpace(target))
		{
			throw new ArgumentException("Search target is empty.");
		}
		int pid = ExtractPid(process);
		byte[] bytes = Encoding.ASCII.GetBytes(target);
		MethodInfo methodInfo = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "GetProcessMaps" && x.GetParameters().Length == 1) ?? throw new MissingMethodException(apiType.FullName, "GetProcessMaps(pid)");
		object o;
		try
		{
			o = methodInfo.Invoke(api, new object[1] { pid });
		}
		catch (TargetInvocationException ex)
		{
			throw ex.InnerException ?? ex;
		}
		IEnumerable source = ((GetMemberValue(o, "entries") ?? GetMemberValue(o, "Entries")) as IEnumerable) ?? throw new InvalidOperationException("Process map entries were not enumerable.");
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		List<ulong> list = new List<ulong>();
		List<string> list2 = new List<string> { $"ASCII search started: target='{target}', pid={pid}, bytes={bytes.Length}" };
		foreach (object item in from object x in source
			where x != null
			select x)
		{
			ulong num = ToUInt64(GetMemberValue(item, "start") ?? GetMemberValue(item, "Start"));
			ulong num2 = ToUInt64(GetMemberValue(item, "end") ?? GetMemberValue(item, "End"));
			uint num3 = Convert.ToUInt32(GetMemberValue(item, "prot") ?? GetMemberValue(item, "Prot") ?? ((object)0u));
			if (num == 0L || num2 <= num || (num3 & 1) == 0)
			{
				continue;
			}
			ulong num4 = num2 - num;
			byte[] array = Array.Empty<byte>();
			for (ulong num5 = 0uL; num5 < num4; num5 += (ulong)(262144 - Math.Max(bytes.Length, 32)))
			{
				int num6 = (int)Math.Min(262144uL, num4 - num5);
				byte[] array2;
				try
				{
					array2 = Read(num + num5, num6);
				}
				catch
				{
					break;
				}
				if (array2.Length == 0)
				{
					break;
				}
				byte[] array3 = new byte[array.Length + array2.Length];
				Buffer.BlockCopy(array, 0, array3, 0, array.Length);
				Buffer.BlockCopy(array2, 0, array3, array.Length, array2.Length);
				ulong num7 = num + num5 - (ulong)array.Length;
				for (int num8 = 0; num8 <= array3.Length - bytes.Length; num8++)
				{
					bool flag = true;
					for (int num9 = 0; num9 < bytes.Length; num9++)
					{
						if (array3[num8 + num9] != bytes[num9])
						{
							flag = false;
							break;
						}
					}
					if (!flag)
					{
						continue;
					}
					ulong num10 = num7 + (ulong)num8;
					if (!list.Contains(num10))
					{
						list.Add(num10);
						list2.Add($"ASCII HIT: 0x{num10:X}  '{target}'");
						if (list.Count >= 32)
						{
							break;
						}
					}
				}
				if (list.Count >= 32)
				{
					break;
				}
				int num11 = Math.Min(Math.Max(bytes.Length - 1, 32), array2.Length);
				array = array2.Skip(array2.Length - num11).ToArray();
				if (array2.Length < num6)
				{
					break;
				}
			}
			if (list.Count >= 32)
			{
				break;
			}
		}
		if (list.Count == 0)
		{
			list2.Add("ASCII search complete: no exact matches for '" + target + "'.");
		}
		else
		{
			list2.Add($"ASCII search complete: {list.Count} exact match(es). No game memory was modified.");
		}
		return list2;
		byte[] Read(ulong a, int n)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, a, n }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex2)
			{
				throw ex2.InnerException ?? ex2;
			}
		}
	}

	public List<ulong> FindAsciiHits(object process, string target, int maxHits = 32, int chunkSize = 262144)
	{
		if (string.IsNullOrEmpty(target))
		{
			throw new ArgumentException("Search target is empty.", "target");
		}
		int pid = ExtractPid(process);
		byte[] bytes = Encoding.ASCII.GetBytes(target);
		MethodInfo methodInfo = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "GetProcessMaps" && x.GetParameters().Length == 1) ?? throw new MissingMethodException(apiType.FullName, "GetProcessMaps(pid)");
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		object o;
		try
		{
			o = methodInfo.Invoke(api, new object[1] { pid });
		}
		catch (TargetInvocationException ex)
		{
			throw ex.InnerException ?? ex;
		}
		IEnumerable source = ((GetMemberValue(o, "entries") ?? GetMemberValue(o, "Entries")) as IEnumerable) ?? throw new InvalidOperationException("Process map entries were not enumerable.");
		List<ulong> list = new List<ulong>();
		foreach (object item in from object x in source
			where x != null
			select x)
		{
			ulong num = ToUInt64(GetMemberValue(item, "start") ?? GetMemberValue(item, "Start"));
			ulong num2 = ToUInt64(GetMemberValue(item, "end") ?? GetMemberValue(item, "End"));
			if (num == 0L || num2 <= num)
			{
				continue;
			}
			uint num3 = 0u;
			bool flag = false;
			object obj = GetMemberValue(item, "prot") ?? GetMemberValue(item, "Prot");
			if (obj != null)
			{
				try
				{
					num3 = Convert.ToUInt32(obj);
					flag = true;
				}
				catch
				{
				}
			}
			if (flag && (num3 & 2) == 0)
			{
				continue;
			}
			ulong num4 = num2 - num;
			byte[] array = Array.Empty<byte>();
			for (ulong num5 = 0uL; num5 < num4; num5 += (ulong)chunkSize)
			{
				if (list.Count >= maxHits)
				{
					break;
				}
				int n = (int)Math.Min((ulong)chunkSize, num4 - num5);
				byte[] array2;
				try
				{
					array2 = Read(num + num5, n);
				}
				catch
				{
					break;
				}
				if (array2.Length == 0)
				{
					break;
				}
				byte[] array3 = new byte[array.Length + array2.Length];
				Buffer.BlockCopy(array, 0, array3, 0, array.Length);
				Buffer.BlockCopy(array2, 0, array3, array.Length, array2.Length);
				ulong num6 = num + num5 - (ulong)array.Length;
				for (int num7 = 0; num7 <= array3.Length - bytes.Length; num7++)
				{
					if (list.Count >= maxHits)
					{
						break;
					}
					bool flag2 = true;
					for (int num8 = 0; num8 < bytes.Length; num8++)
					{
						if (array3[num7 + num8] != bytes[num8])
						{
							flag2 = false;
							break;
						}
					}
					if (flag2)
					{
						list.Add(num6 + (ulong)num7);
					}
				}
				int num9 = Math.Min(Math.Max(bytes.Length - 1, 32), array2.Length);
				array = array3.Skip(array3.Length - num9).ToArray();
			}
		}
		return list;
		byte[] Read(ulong a, int num10)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, a, num10 }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex2)
			{
				throw ex2.InnerException ?? ex2;
			}
		}
	}

	public List<string> AnalyzeKnownPlayerNameHits(object process, string target)
	{
		int pid = ExtractPid(process);
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		ulong[] array = new ulong[32]
		{
			20718920uL, 20885552uL, 20886112uL, 21586424uL, 30248784uL, 32127980uL, 41642756uL, 41763505uL, 43011867uL, 44646059uL,
			47338356uL, 47341944uL, 53068996uL, 63930799uL, 63858577uL, 17238212427uL, 17238212445uL, 17238212463uL, 17238212481uL, 17238212499uL,
			17238212517uL, 17238212535uL, 17238212553uL, 17238212571uL, 17238212589uL, 17238212607uL, 17238212625uL, 17238212643uL, 17238212661uL, 17238212679uL,
			17238212697uL, 17238212715uL
		};
		List<string> list = new List<string> { $"Targeted hit analysis started: pid={pid}, target='{target}', knownHits={array.Length}" };
		ulong num = 0uL;
		ulong[] array2 = array;
		foreach (ulong num3 in array2)
		{
			string value = ((num == 0L) ? "n/a" : $"0x{num3 - num:X}");
			try
			{
				ulong a = ((num3 >= 128) ? (num3 - 128) : 0);
				string value2 = FindPrintableStrings(Read(a, 288));
				byte[] source = Read(num3, Math.Max(16, target.Length));
				string text = Encoding.ASCII.GetString(source.Take(target.Length).ToArray());
				list.Add($"HIT 0x{num3:X} delta={value} exact={((text == target) ? "YES" : "NO")} nearby=[{value2}]");
			}
			catch (Exception ex)
			{
				list.Add($"HIT 0x{num3:X} delta={value} READ FAIL: {ex.GetBaseException().Message}");
			}
			num = num3;
		}
		ulong[] array3 = (from x in array
			where x >= 17238196224L && x < 17238261760L
			orderby x
			select x).ToArray();
		if (array3.Length > 1)
		{
			ulong[] array4 = array3.Zip(array3.Skip(1), (ulong num4, ulong b) => b - num4).ToArray();
			list.Add("0x4037A cluster deltas: " + string.Join(", ", array4.Select((ulong x) => $"0x{x:X}")));
			IGrouping<ulong, ulong> grouping = (from x in array4
				group x by x into g
				orderby g.Count() descending
				select g).First();
			list.Add($"0x4037A dominant spacing: 0x{grouping.Key:X} ({grouping.Count()}/{array4.Length} deltas)");
		}
		list.Add("Targeted hit analysis complete. No full-memory scan was performed; no game memory was modified.");
		return list;
		byte[] Read(ulong num4, int n)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, num4, n }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex2)
			{
				throw ex2.InnerException ?? ex2;
			}
		}
	}

	public List<string> CompareTwoPlayerCandidates(object process, string player1, string player2)
	{
		int pid = ExtractPid(process);
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		ulong[] obj = new ulong[11]
		{
			20718920uL, 20885552uL, 20886112uL, 21586424uL, 32127980uL, 41763505uL, 43011867uL, 44646059uL, 47338356uL, 47341944uL,
			53068996uL
		};
		byte[] bytes = Encoding.ASCII.GetBytes(player1);
		byte[] bytes2 = Encoding.ASCII.GetBytes(player2);
		SortedSet<ulong> sortedSet = new SortedSet<ulong>();
		SortedSet<ulong> h2 = new SortedSet<ulong>();
		List<string> list = new List<string> { $"Two-player candidate comparison: pid={pid}, P1='{player1}', P2='{player2}'" };
		ulong[] array = obj;
		foreach (ulong num2 in array)
		{
			try
			{
				ulong num3 = ((num2 >= 65536) ? (num2 - 65536) : 0);
				byte[] array2 = Read(num3, 131072);
				int num4 = FindAll(array2, bytes, num3, sortedSet);
				int num5 = FindAll(array2, bytes2, num3, h2);
				if (num4 + num5 > 0)
				{
					list.Add($"REGION 0x{num3:X}-0x{(ulong)((long)num3 + (long)array2.Length):X}: P1 hits={num4}, P2 hits={num5}");
				}
			}
			catch (Exception ex)
			{
				list.Add($"REGION near 0x{num2:X} READ FAIL: {ex.GetBaseException().Message}");
			}
		}
		list.Add("P1 exact hits: " + ((sortedSet.Count == 0) ? "none" : string.Join(", ", sortedSet.Select((ulong x) => $"0x{x:X}"))));
		list.Add("P2 exact hits: " + ((h2.Count == 0) ? "none" : string.Join(", ", h2.Select((ulong x) => $"0x{x:X}"))));
		if (sortedSet.Count > 0 && h2.Count > 0)
		{
			(ulong, ulong, ulong)[] array3 = (from a in sortedSet
				from b in h2
				let delta = (a > b) ? (a - b) : (b - a)
				orderby delta
				select (a: a, b: b, delta: delta)).Take(12).ToArray();
			list.Add("Closest cross-player name pairs:");
			(ulong, ulong, ulong)[] array4 = array3;
			for (int num = 0; num < array4.Length; num++)
			{
				(ulong, ulong, ulong) tuple = array4[num];
				list.Add($"  P1=0x{tuple.Item1:X} P2=0x{tuple.Item2:X} delta=0x{tuple.Item3:X}");
			}
			IGrouping<ulong, (ulong, ulong, ulong)> grouping = (from x in array3
				group x by x.delta into g
				orderby g.Count() descending, g.Key
				select g).First();
			list.Add($"Best repeated candidate spacing in sampled regions: 0x{grouping.Key:X} ({grouping.Count()} pair(s))");
		}
		else
		{
			list.Add("Both names were not found in the bounded candidate regions; no stride conclusion made.");
		}
		list.Add("Two-player comparison complete. Bounded reads only; no full-memory scan and no game memory was modified.");
		return list;
		static int FindAll(byte[] b, byte[] n, ulong start, SortedSet<ulong> dst)
		{
			int num6 = 0;
			if (n.Length == 0)
			{
				return 0;
			}
			for (int i = 0; i + n.Length <= b.Length; i++)
			{
				bool flag = true;
				for (int j = 0; j < n.Length; j++)
				{
					if (b[i + j] != n[j])
					{
						flag = false;
						break;
					}
				}
				if (flag)
				{
					dst.Add(start + (ulong)i);
					num6++;
				}
			}
			return num6;
		}
		byte[] Read(ulong a, int n)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, a, n }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex2)
			{
				throw ex2.InnerException ?? ex2;
			}
		}
	}

	public List<RegionSnapshot> ReadCandidateRegions(object process)
	{
		int pid = ExtractPid(process);
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		ulong[] source = new ulong[14]
		{
			20718920uL, 20885552uL, 20886112uL, 21586424uL, 30248784uL, 32127980uL, 41642756uL, 41763505uL, 43011867uL, 44646059uL,
			47338356uL, 47341944uL, 47382016uL, 53068996uL
		};
		List<(ulong, ulong)> list = new List<(ulong, ulong)>();
		foreach (ulong item in source.OrderBy((ulong x) => x))
		{
			ulong num = ((item > 32768) ? (item - 32768) : 0);
			ulong num2 = item + 32768;
			if (list.Count > 0)
			{
				if (num <= list[list.Count - 1].Item2)
				{
					list[list.Count - 1] = (list[list.Count - 1].Item1, Math.Max(list[list.Count - 1].Item2, num2));
					continue;
				}
			}
			list.Add((num, num2));
		}
		List<RegionSnapshot> list2 = new List<RegionSnapshot>();
		foreach (var item2 in list)
		{
			var (num3, _) = item2;
			for (; num3 < item2.Item2; num3 += 65536)
			{
				int n = (int)Math.Min(65536uL, item2.Item2 - num3);
				try
				{
					byte[] array = Read(num3, n);
					if (array.Length != 0)
					{
						list2.Add(new RegionSnapshot(num3, array));
					}
				}
				catch
				{
				}
			}
		}
		if (list2.Count == 0)
		{
			throw new InvalidOperationException("No bounded candidate regions could be read.");
		}
		return list2;
		byte[] Read(ulong a, int num4)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, a, num4 }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex)
			{
				throw ex.InnerException ?? ex;
			}
		}
	}

	public byte ReadByte(object process, ulong address)
	{
		int num = ExtractPid(process);
		MethodInfo methodInfo = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		try
		{
			byte[] array = (byte[])(methodInfo.Invoke(api, new object[3] { num, address, 1 }) ?? Array.Empty<byte>());
			if (array.Length != 1)
			{
				throw new InvalidOperationException($"Read returned {array.Length} byte(s), expected 1.");
			}
			return array[0];
		}
		catch (TargetInvocationException ex)
		{
			throw ex.InnerException ?? ex;
		}
	}

	public void WriteByteRestricted(object process, ulong address, byte value)
	{
		bool num = address == 21586608 || address == 21586612;
		bool flag = address == 44668098 || address == 47316587 || address == 47323806;
		if (!num && !flag)
		{
			throw new InvalidOperationException($"Restricted writer refuses address 0x{address:X}; it is not one of the five validated candidates.");
		}
		if (num && value != 1 && value != 2)
		{
			throw new InvalidOperationException($"Restricted writer refuses value {value:X2}; this candidate permits only 01/02.");
		}
		if (flag && value != 32 && value != 64)
		{
			throw new InvalidOperationException($"Restricted writer refuses value {value:X2}; this candidate permits only 20/40.");
		}
		int num2 = ExtractPid(process);
		MethodInfo[] array = (from x in apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
			where x.Name == "WriteMemory" && x.GetParameters().Length == 3
			select x).ToArray();
		if (array.Length == 0)
		{
			throw new MissingMethodException(apiType.FullName, "WriteMemory(pid, address, byte[])");
		}
		Exception innerException = null;
		MethodInfo[] array2 = array;
		foreach (MethodInfo methodInfo in array2)
		{
			try
			{
				ParameterInfo[] parameters = methodInfo.GetParameters();
				object obj = Convert.ChangeType(num2, parameters[0].ParameterType);
				object obj2 = Convert.ChangeType(address, parameters[1].ParameterType);
				if (parameters[2].ParameterType == typeof(byte[]))
				{
					object obj3 = new byte[1] { value };
					methodInfo.Invoke(api, new object[3] { obj, obj2, obj3 });
					return;
				}
			}
			catch (TargetInvocationException ex)
			{
				innerException = ex.InnerException ?? ex;
			}
			catch (Exception ex2)
			{
				innerException = ex2;
			}
		}
		throw new InvalidOperationException("No compatible WriteMemory(pid,address,byte[]) overload succeeded.", innerException);
	}

	public int GetPid(object process)
	{
		return ExtractPid(process);
	}

	public void WriteMemory(object process, ulong address, byte[] data)
	{
		int num = ExtractPid(process);
		MethodInfo[] array = (from x in apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
			where x.Name == "WriteMemory" && x.GetParameters().Length == 3
			select x).ToArray();
		if (array.Length == 0)
		{
			throw new MissingMethodException(apiType.FullName, "WriteMemory(pid, address, byte[])");
		}
		Exception innerException = null;
		MethodInfo[] array2 = array;
		foreach (MethodInfo methodInfo in array2)
		{
			try
			{
				ParameterInfo[] parameters = methodInfo.GetParameters();
				object obj = Convert.ChangeType(num, parameters[0].ParameterType);
				object obj2 = Convert.ChangeType(address, parameters[1].ParameterType);
				if (parameters[2].ParameterType == typeof(byte[]))
				{
					methodInfo.Invoke(api, new object[3] { obj, obj2, data });
					return;
				}
			}
			catch (TargetInvocationException ex)
			{
				innerException = ex.InnerException ?? ex;
			}
			catch (Exception ex2)
			{
				innerException = ex2;
			}
		}
		throw new InvalidOperationException("No compatible WriteMemory(pid,address,byte[]) overload succeeded.", innerException);
	}

	public Dictionary<ulong, byte> ReadValidationCandidates(object process)
	{
		int num = ExtractPid(process);
		MethodInfo methodInfo = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		ulong[] obj = new ulong[5] { 21586608uL, 21586612uL, 44668098uL, 47316587uL, 47323806uL };
		Dictionary<ulong, byte> dictionary = new Dictionary<ulong, byte>();
		ulong[] array = obj;
		foreach (ulong num3 in array)
		{
			try
			{
				byte[] array2 = (byte[])(methodInfo.Invoke(api, new object[3] { num, num3, 1 }) ?? Array.Empty<byte>());
				if (array2.Length == 1)
				{
					dictionary[num3] = array2[0];
				}
			}
			catch (TargetInvocationException ex)
			{
				throw ex.InnerException ?? ex;
			}
		}
		return dictionary;
	}

	public PlayerWindowRead ReadPlayerWindow(object process, int slot, int bytesBefore, int bytesAfter)
	{
		if (slot < 0 || slot >= 18)
		{
			throw new ArgumentOutOfRangeException("slot");
		}
		if (bytesBefore < 0 || bytesAfter <= 0)
		{
			throw new ArgumentOutOfRangeException("Window sizes must be positive.");
		}
		int num = ExtractPid(process);
		MethodInfo methodInfo = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		ulong num2 = 47382016uL + (ulong)(slot * 128);
		if ((ulong)bytesBefore > num2)
		{
			throw new InvalidOperationException("Expanded window would underflow address space.");
		}
		ulong num3 = num2 - (ulong)bytesBefore;
		int num4 = checked(bytesBefore + bytesAfter);
		try
		{
			byte[] array = (byte[])(methodInfo.Invoke(api, new object[3] { num, num3, num4 }) ?? Array.Empty<byte>());
			if (array.Length != num4)
			{
				throw new InvalidOperationException($"Expected {num4} expanded bytes for slot {slot:00}, received {array.Length}.");
			}
			return new PlayerWindowRead(num3, array);
		}
		catch (TargetInvocationException ex)
		{
			throw ex.InnerException ?? ex;
		}
	}

	public byte[] ReadPlayerRecord80(object process, int slot)
	{
		if (slot < 0 || slot >= 18)
		{
			throw new ArgumentOutOfRangeException("slot");
		}
		int num = ExtractPid(process);
		MethodInfo methodInfo = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		ulong num2 = 47382016uL + (ulong)(slot * 128);
		try
		{
			byte[] array = (byte[])(methodInfo.Invoke(api, new object[3] { num, num2, 128 }) ?? Array.Empty<byte>());
			if (array.Length != 128)
			{
				throw new InvalidOperationException($"Expected 128 bytes for slot {slot:00}, received {array.Length}.");
			}
			return array;
		}
		catch (TargetInvocationException ex)
		{
			throw ex.InnerException ?? ex;
		}
	}

	public byte[] ReadAllPlayerRecords80(object process)
	{
		byte[] array = new byte[2304];
		for (int i = 0; i < 18; i++)
		{
			Buffer.BlockCopy(ReadPlayerRecord80(process, i), 0, array, i * 128, 128);
		}
		return array;
	}

	public PlayerRead80Result ReadPlayers80(object process, string player1, string player2)
	{
		int pid = ExtractPid(process);
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		byte[] bytes = Encoding.ASCII.GetBytes(player1);
		byte[] bytes2 = Encoding.ASCII.GetBytes(player2);
		PlayerRead80Result playerRead80Result = new PlayerRead80Result();
		playerRead80Result.Diagnostics.Add($"V3.7.0 Players reader: pid={pid}, base=0x{47382016uL:X}, stride=0x80, slots=18");
		byte[] array = Read(47382016uL, 2304);
		for (int num = 0; num < 18; num++)
		{
			byte[] b = array.Skip(num * 128).Take(Math.Min(128, Math.Max(0, array.Length - num * 128))).ToArray();
			int num2 = Find(b, bytes);
			int num3 = Find(b, bytes2);
			string text = ((num2 >= 0) ? player1 : ((num3 >= 0) ? player2 : "<empty>"));
			if (text != "<empty>")
			{
				playerRead80Result.Active++;
			}
			playerRead80Result.Names.Add(text);
			string value = ((num2 >= 0) ? $"0x{num2:X}" : ((num3 >= 0) ? $"0x{num3:X}" : "n/a"));
			playerRead80Result.Diagnostics.Add($"PLAYER80 slot={num:00} base=0x{(ulong)(47382016L + (long)(num * 128)):X} state={((text == "<empty>") ? "EMPTY/OTHER" : "OCCUPIED")} name='{text}' nameOff={value}");
		}
		bool value2 = playerRead80Result.Names.Count >= 2 && playerRead80Result.Names[0] == player1 && playerRead80Result.Names[1] == player2;
		playerRead80Result.Diagnostics.Add($"PLAYER80 validation: slot0=P1/slot1=P2 adjacent={value2}; storedNameMatches={playerRead80Result.Active}; live presence unverified");
		for (int num4 = 0; num4 < Math.Min(18, playerRead80Result.Names.Count); num4++)
		{
			byte[] rec;
			if (!(playerRead80Result.Names[num4] == "<empty>"))
			{
				rec = array.Skip(num4 * 128).Take(128).ToArray();
				StringBuilder stringBuilder = new StringBuilder();
				byte[] array2 = rec;
				foreach (byte b2 in array2)
				{
					stringBuilder.Append((char)((b2 >= 32 && b2 <= 126) ? b2 : 46));
				}
				playerRead80Result.Diagnostics.Add($"FIELD80 slot={num4:00} base=0x{(ulong)(47382016L + (long)(num4 * 128)):X} b00-0F={Hex(0, 16)}");
				playerRead80Result.Diagnostics.Add($"FIELD80 slot={num4:00} u32@10=0x{U(16):X8} u32@20=0x{U(32):X8} u32@30=0x{U(48):X8} u32@40=0x{U(64):X8} u64@70=0x{U64(112):X16}");
				playerRead80Result.Diagnostics.Add($"FIELD80 slot={num4:00} ascii={stringBuilder}");
			}
			string Hex(int off, int len)
			{
				if (off < rec.Length)
				{
					return BitConverter.ToString(rec, off, Math.Min(len, rec.Length - off)).Replace("-", "");
				}
				return "";
			}
			uint U(int off)
			{
				if (off + 4 > rec.Length)
				{
					return 0u;
				}
				return BitConverter.ToUInt32(rec, off);
			}
			ulong U64(int off)
			{
				if (off + 8 > rec.Length)
				{
					return 0uL;
				}
				return BitConverter.ToUInt64(rec, off);
			}
		}
		if (playerRead80Result.Names.Count >= 2 && playerRead80Result.Names[0] != "<empty>" && playerRead80Result.Names[1] != "<empty>")
		{
			byte[] array3 = array.Take(128).ToArray();
			byte[] array4 = array.Skip(128).Take(128).ToArray();
			List<string> list = new List<string>();
			for (int num6 = 0; num6 < Math.Min(array3.Length, array4.Length); num6++)
			{
				if (array3[num6] != array4[num6])
				{
					list.Add($"{num6:X2}:{array3[num6]:X2}>{array4[num6]:X2}");
				}
			}
			playerRead80Result.Diagnostics.Add($"DIFF80 slot0-vs-slot1 differingBytes={list.Count}/128 first={string.Join(",", list.Take(48))}");
			List<string> list2 = new List<string>();
			int num7 = -1;
			for (int num8 = 0; num8 <= Math.Min(array3.Length, array4.Length); num8++)
			{
				int num9;
				if (num8 < Math.Min(array3.Length, array4.Length))
				{
					num9 = ((array3[num8] != array4[num8]) ? 1 : 0);
					if (num9 != 0 && num7 < 0)
					{
						num7 = num8;
					}
				}
				else
				{
					num9 = 0;
				}
				if (num9 == 0 && num7 >= 0)
				{
					list2.Add($"0x{num7:X2}-0x{num8 - 1:X2}({num8 - num7})");
					num7 = -1;
				}
			}
			playerRead80Result.Diagnostics.Add("MAP80 changed-runs=" + string.Join(",", list2));
			for (int num10 = 0; num10 + 1 < 128; num10 += 2)
			{
				ushort num11 = BitConverter.ToUInt16(array3, num10);
				ushort num12 = BitConverter.ToUInt16(array4, num10);
				if (num11 != num12)
				{
					playerRead80Result.Diagnostics.Add($"MAP80 u16@0x{num10:X2} P1=0x{num11:X4} P2=0x{num12:X4}");
				}
			}
			for (int num13 = 0; num13 + 3 < 128; num13 += 4)
			{
				uint num14 = BitConverter.ToUInt32(array3, num13);
				uint num15 = BitConverter.ToUInt32(array4, num13);
				if (num14 != num15)
				{
					playerRead80Result.Diagnostics.Add($"MAP80 u32@0x{num13:X2} P1=0x{num14:X8} P2=0x{num15:X8}");
				}
			}
			for (int num16 = 0; num16 + 7 < 128; num16 += 8)
			{
				ulong num17 = BitConverter.ToUInt64(array3, num16);
				ulong num18 = BitConverter.ToUInt64(array4, num16);
				if (num17 != num18)
				{
					playerRead80Result.Diagnostics.Add($"MAP80 u64@0x{num16:X2} P1=0x{num17:X16} P2=0x{num18:X16}");
				}
			}
		}
		playerRead80Result.Diagnostics.Add("V3.7.2 differential map complete. Values are not assigned meanings yet; no game memory was modified.");
		return playerRead80Result;
		static int Find(byte[] array5, byte[] n)
		{
			if (n.Length == 0 || array5.Length < n.Length)
			{
				return -1;
			}
			for (int i = 0; i <= array5.Length - n.Length; i++)
			{
				bool flag = true;
				for (int j = 0; j < n.Length; j++)
				{
					if (array5[i + j] != n[j])
					{
						flag = false;
						break;
					}
				}
				if (flag)
				{
					return i;
				}
			}
			return -1;
		}
		byte[] Read(ulong a, int n)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, a, n }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex)
			{
				throw ex.InnerException ?? ex;
			}
		}
	}

	public List<string> LiveNameNeighborhood(object process, string playerName)
	{
		int pid = ExtractPid(process);
		List<string> list = new List<string>();
		if (string.IsNullOrWhiteSpace(playerName))
		{
			list.Add("RESULT no player name supplied.");
			return list;
		}
		byte[] bytes = Encoding.ASCII.GetBytes(playerName);
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid,address,length)");
		ulong[] array = new ulong[3] { 41118468uL, 53068996uL, 63694457uL };
		list.Add($"START pid={pid} name='{playerName}' anchors={array.Length} window=+/-0x400 read-only");
		int num = 0;
		ulong[] array2 = array;
		foreach (ulong num3 in array2)
		{
			try
			{
				ulong num4 = ((num3 >= 1024) ? (num3 - 1024) : 0);
				byte[] array3 = Read(num4, 2048);
				int num5 = Find(array3, bytes);
				bool flag = num5 >= 0;
				if (flag)
				{
					num++;
				}
				list.Add($"HIT anchor=0x{num3:X} exactNow={flag} currentOffset={((num5 >= 0) ? $"0x{num5:X}" : "n/a")}");
				if (num5 < 0)
				{
					continue;
				}
				int num6 = Math.Max(0, num5 - 256);
				int num7 = Math.Min(576, array3.Length - num6);
				byte[] b = array3.Skip(num6).Take(num7).ToArray();
				list.Add($"STRINGS anchor=0x{num3:X} range=0x{(ulong)((long)num4 + (long)num6):X}-0x{(ulong)((long)num4 + (long)(num6 + num7)):X} [{Strings(b)}]");
				for (int num8 = -512; num8 <= 512; num8 += 128)
				{
					long num9 = (long)num3 + (long)num8;
					if (num9 >= 0)
					{
						ulong num10 = (ulong)num9;
						byte[] b2 = Read(num10, 128);
						list.Add($"REC80 anchor=0x{num3:X} delta={((num8 >= 0) ? "+" : "-")}0x{Math.Abs(num8):X} base=0x{num10:X} strings=[{Strings(b2)}]");
					}
				}
			}
			catch (Exception ex)
			{
				list.Add($"ANCHOR 0x{num3:X} READ FAIL: {ex.GetBaseException().Message}");
			}
		}
		list.Add($"SUMMARY exact anchors still live={num}/{array.Length}. Compare printable strings and repeating 0x80 neighborhoods before adding Player 2.");
		list.Add("RESULT neighborhood capture complete; no game memory modified.");
		return list;
		static int Find(byte[] array4, byte[] n)
		{
			for (int i = 0; i <= array4.Length - n.Length; i++)
			{
				bool flag2 = true;
				for (int j = 0; j < n.Length; j++)
				{
					if (array4[i + j] != n[j])
					{
						flag2 = false;
						break;
					}
				}
				if (flag2)
				{
					return i;
				}
			}
			return -1;
		}
		byte[] Read(ulong a, int n)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, a, n }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex2)
			{
				throw ex2.InnerException ?? ex2;
			}
		}
		static string Strings(byte[] array4)
		{
			List<string> list2 = new List<string>();
			int i = 0;
			while (i < array4.Length)
			{
				for (; i < array4.Length && (array4[i] < 32 || array4[i] > 126); i++)
				{
				}
				int num11 = i;
				for (; i < array4.Length && array4[i] >= 32 && array4[i] <= 126; i++)
				{
				}
				if (i - num11 >= 4)
				{
					list2.Add(Encoding.ASCII.GetString(array4, num11, i - num11));
				}
			}
			if (list2.Count != 0)
			{
				return string.Join(" | ", list2.Take(16));
			}
			return "<none>";
		}
	}

	public List<string> ExactNameLocator(object process, string playerName)
	{
		int pid = ExtractPid(process);
		List<string> list = new List<string>();
		if (string.IsNullOrWhiteSpace(playerName))
		{
			list.Add("RESULT no player name supplied.");
			return list;
		}
		byte[] bytes = Encoding.ASCII.GetBytes(playerName);
		MethodInfo methodInfo = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "GetProcessMaps" && x.GetParameters().Length == 1) ?? throw new MissingMethodException(apiType.FullName, "GetProcessMaps(pid)");
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		object obj;
		try
		{
			obj = methodInfo.Invoke(api, new object[1] { pid });
		}
		catch (TargetInvocationException ex)
		{
			throw ex.InnerException ?? ex;
		}
		IEnumerable source = (((obj == null) ? null : (GetMemberValue(obj, "entries") ?? GetMemberValue(obj, "Entries"))) as IEnumerable) ?? throw new InvalidOperationException("Process map entries were not enumerable.");
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		list.Add($"START exact ASCII locator pid={pid} name='{playerName}' range=0x{33554432uL:X}-0x{67108864uL:X} chunk=0x{262144:X} read-only");
		foreach (object item in from object x in source
			where x != null
			select x)
		{
			ulong num5 = ToUInt64(GetMemberValue(item, "start") ?? GetMemberValue(item, "Start"));
			ulong num6 = ToUInt64(GetMemberValue(item, "end") ?? GetMemberValue(item, "End"));
			if (num5 == 0L || num6 <= num5 || num6 <= 33554432 || num5 >= 67108864)
			{
				continue;
			}
			ulong num7 = Math.Max(num5, 33554432uL);
			ulong num8 = Math.Min(num6, 67108864uL);
			num++;
			list.Add($"MAP 0x{num7:X}-0x{num8:X} size=0x{num8 - num7:X}");
			ulong num9 = num7;
			byte[] array = Array.Empty<byte>();
			while (num9 < num8)
			{
				int num10 = (int)Math.Min(262144uL, num8 - num9);
				byte[] array2;
				try
				{
					array2 = Read(num9, num10);
					num2++;
				}
				catch
				{
					num4++;
					num9 += (ulong)num10;
					array = Array.Empty<byte>();
					continue;
				}
				if (array2.Length == 0)
				{
					num9 += (ulong)num10;
					array = Array.Empty<byte>();
					continue;
				}
				byte[] array3 = new byte[array.Length + array2.Length];
				Buffer.BlockCopy(array, 0, array3, 0, array.Length);
				Buffer.BlockCopy(array2, 0, array3, array.Length, array2.Length);
				int start = 0;
				do
				{
					int num11 = FindAt(array3, bytes, start);
					if (num11 < 0)
					{
						break;
					}
					ulong value = (ulong)((long)num9 - (long)array.Length + num11);
					num3++;
					list.Add($"HIT {num3}: address=0x{value:X} map=0x{num5:X}-0x{num6:X}");
					start = num11 + 1;
				}
				while (num3 < 32);
				if (num3 >= 32)
				{
					break;
				}
				int num12 = Math.Min(Math.Max(0, bytes.Length - 1), array3.Length);
				array = array3.Skip(array3.Length - num12).ToArray();
				num9 += (ulong)array2.Length;
				if (array2.Length < num10)
				{
					num9 += (ulong)(num10 - array2.Length);
				}
			}
			if (num3 >= 32)
			{
				break;
			}
		}
		list.Add($"SUMMARY name='{playerName}' hits={num3} maps={num} chunks={num2} readFailures={num4}");
		list.Add((num3 == 0) ? "RESULT exact name not found in targeted 0x02000000-0x04000000 range." : "RESULT exact name hit(s) found. Keep Player 2 out and send these HIT addresses.");
		list.Add("Exact-name locator complete. Read-only; no game memory modified.");
		return list;
		static int FindAt(byte[] b, byte[] n, int val)
		{
			for (int i = Math.Max(0, val); i <= b.Length - n.Length; i++)
			{
				bool flag = true;
				for (int j = 0; j < n.Length; j++)
				{
					if (b[i + j] != n[j])
					{
						flag = false;
						break;
					}
				}
				if (flag)
				{
					return i;
				}
			}
			return -1;
		}
		byte[] Read(ulong a, int n)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, a, n }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex2)
			{
				throw ex2.InnerException ?? ex2;
			}
		}
	}

	public List<string> Enumerate80Occupancy(object process, string player1, string player2)
	{
		int pid = ExtractPid(process);
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		(ulong, ulong)[] array = new(ulong, ulong)[5]
		{
			(44667997uL, 44668130uL),
			(47316486uL, 47316619uL),
			(47323705uL, 47323838uL),
			(47382022uL, 47382155uL),
			(47389241uL, 47389374uL)
		};
		byte[] bytes = Encoding.ASCII.GetBytes(player1);
		byte[] bytes2 = Encoding.ASCII.GetBytes(player2);
		List<string> list = new List<string> { $"0x80 occupancy enumeration: pid={pid}, P1='{player1}', P2='{player2}', anchors={array.Length}" };
		int num = 0;
		(ulong, ulong)[] array2 = array;
		for (int num2 = 0; num2 < array2.Length; num2++)
		{
			(ulong, ulong) tuple = array2[num2];
			try
			{
				ulong num3 = tuple.Item1 & 0xFFFFFFFFFFFFFF80uL;
				ulong start = ((num3 >= 384) ? (num3 - 384) : 0);
				byte[] win = Read(start, 1536);
				list.Add($"ANCHOR80 P1=0x{tuple.Item1:X} P2=0x{tuple.Item2:X} base=0x{num3:X} nameDelta=0x{tuple.Item2 - tuple.Item1:X}");
				bool flag = false;
				bool flag2 = false;
				int num4 = 999;
				int num5 = 999;
				for (int num6 = -3; num6 <= 8; num6++)
				{
					ulong num7 = ((num6 >= 0) ? (num3 + (ulong)(num6 * 128)) : ((num3 >= (ulong)(-num6 * 128)) ? (num3 - (ulong)(-num6 * 128)) : 0));
					byte[] array3 = Slice(num7);
					if (array3.Length != 0)
					{
						int num8 = Find(array3, bytes);
						int num9 = Find(array3, bytes2);
						string value = ((num8 >= 0 && num9 >= 0) ? "BOTH" : ((num8 >= 0) ? "P1" : ((num9 >= 0) ? "P2" : "EMPTY/OTHER")));
						if (num8 >= 0)
						{
							flag = true;
							num4 = num6;
						}
						if (num9 >= 0)
						{
							flag2 = true;
							num5 = num6;
						}
						string value2 = ((num8 >= 0) ? $" P1off=0x{num8:X}" : "") + ((num9 >= 0) ? $" P2off=0x{num9:X}" : "");
						list.Add($"  ENUM rec={num6:+#;-#;0} base=0x{num7:X} state={value}{value2} strings=[{FindPrintableStrings(array3)}]");
					}
				}
				bool flag3 = flag && flag2 && Math.Abs(num5 - num4) == 1;
				if (flag3)
				{
					num++;
				}
				list.Add($"  RESULT P1rec={num4} P2rec={num5} adjacent0x80={flag3}");
				byte[] Slice(ulong a)
				{
					int num10 = (int)(a - start);
					if (num10 < 0 || num10 >= win.Length)
					{
						return Array.Empty<byte>();
					}
					return win.Skip(num10).Take(Math.Min(128, win.Length - num10)).ToArray();
				}
			}
			catch (Exception ex)
			{
				list.Add($"ANCHOR80 0x{tuple.Item1:X}/0x{tuple.Item2:X} READ FAIL: {ex.GetBaseException().Message}");
			}
		}
		list.Add($"SUMMARY adjacent P1/P2 0x80 records: {num}/{array.Length} anchors.");
		list.Add("0x80 occupancy enumeration complete. Bounded read-only windows only; no full-memory scan and no game memory was modified.");
		return list;
		static int Find(byte[] b, byte[] n)
		{
			if (n.Length == 0 || b.Length < n.Length)
			{
				return -1;
			}
			for (int i = 0; i <= b.Length - n.Length; i++)
			{
				bool flag4 = true;
				for (int j = 0; j < n.Length; j++)
				{
					if (b[i + j] != n[j])
					{
						flag4 = false;
						break;
					}
				}
				if (flag4)
				{
					return i;
				}
			}
			return -1;
		}
		byte[] Read(ulong a, int n)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, a, n }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex2)
			{
				throw ex2.InnerException ?? ex2;
			}
		}
	}

	public List<string> Validate80Records(object process, string player1, string player2)
	{
		int pid = ExtractPid(process);
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		(ulong, ulong)[] array = new(ulong, ulong)[5]
		{
			(44667997uL, 44668130uL),
			(47316486uL, 47316619uL),
			(47323705uL, 47323838uL),
			(47382022uL, 47382155uL),
			(47389241uL, 47389374uL)
		};
		byte[] bytes = Encoding.ASCII.GetBytes(player1);
		byte[] bytes2 = Encoding.ASCII.GetBytes(player2);
		List<string> list = new List<string> { $"0x80 record validation: pid={pid}, P1='{player1}', P2='{player2}', anchors={array.Length}" };
		(ulong, ulong)[] array2 = array;
		for (int num = 0; num < array2.Length; num++)
		{
			(ulong, ulong) tuple = array2[num];
			try
			{
				ulong num2 = tuple.Item1 & 0xFFFFFFFFFFFFFF80uL;
				ulong num3 = tuple.Item2 & 0xFFFFFFFFFFFFFF80uL;
				int num4 = (int)(tuple.Item1 - num2);
				int num5 = (int)(tuple.Item2 - num3);
				ulong start = ((num2 >= 256) ? (num2 - 256) : 0);
				int n = (int)(num3 + 512 - start);
				byte[] win = Read(start, n);
				byte[] array3 = Slice(num2, 128);
				byte[] array4 = Slice(num3, 128);
				bool value = num4 + bytes.Length <= array3.Length && array3.Skip(num4).Take(bytes.Length).SequenceEqual(bytes);
				bool value2 = num5 + bytes2.Length <= array4.Length && array4.Skip(num5).Take(bytes2.Length).SequenceEqual(bytes2);
				int num6 = 0;
				int num7 = Math.Min(array3.Length, array4.Length);
				for (int num8 = 0; num8 < num7; num8++)
				{
					if (array3[num8] == array4[num8])
					{
						num6++;
					}
				}
				list.Add($"RECORD anchor P1=0x{tuple.Item1:X} P2=0x{tuple.Item2:X} nameDelta=0x{tuple.Item2 - tuple.Item1:X}");
				list.Add($"  P1base=0x{num2:X} nameOff=0x{num4:X} exact={value}; P2base=0x{num3:X} nameOff=0x{num5:X} exact={value2}; baseDelta=0x{num3 - num2:X}");
				list.Add($"  recordSimilarity={num6}/{num7} ({((num7 != 0) ? (num6 * 100 / num7) : 0)}%)");
				list.Add("  P1 record strings: [" + FindPrintableStrings(array3) + "]");
				list.Add("  P2 record strings: [" + FindPrintableStrings(array4) + "]");
				int[] array5 = new int[6] { -2, -1, 0, 1, 2, 3 };
				foreach (int num10 in array5)
				{
					ulong num11 = ((num10 >= 0) ? (num2 + (ulong)(num10 * 128)) : ((num2 >= (ulong)(-num10 * 128)) ? (num2 - (ulong)(-num10 * 128)) : 0));
					byte[] array6 = Slice(num11, 128);
					if (array6.Length != 0)
					{
						list.Add($"  GRID {num10:+#;-#;0} base=0x{num11:X} strings=[{FindPrintableStrings(array6)}]");
					}
				}
				byte[] Slice(ulong a, int val)
				{
					int num12 = (int)(a - start);
					if (num12 < 0 || num12 >= win.Length)
					{
						return Array.Empty<byte>();
					}
					return win.Skip(num12).Take(Math.Min(val, win.Length - num12)).ToArray();
				}
			}
			catch (Exception ex)
			{
				list.Add($"RECORD 0x{tuple.Item1:X}/0x{tuple.Item2:X} READ FAIL: {ex.GetBaseException().Message}");
			}
		}
		list.Add("0x80 record validation complete. Bounded read-only windows only; no full-memory scan and no game memory was modified.");
		return list;
		byte[] Read(ulong a, int num12)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, a, num12 }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex2)
			{
				throw ex2.InnerException ?? ex2;
			}
		}
	}

	public List<string> AnalyzeRecordBoundaries(object process, string player1, string player2)
	{
		int pid = ExtractPid(process);
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		(ulong, ulong)[] array = new(ulong, ulong)[5]
		{
			(44667997uL, 44668130uL),
			(47316486uL, 47316619uL),
			(47323705uL, 47323838uL),
			(47382022uL, 47382155uL),
			(47389241uL, 47389374uL)
		};
		byte[] bytes = Encoding.ASCII.GetBytes(player1);
		byte[] bytes2 = Encoding.ASCII.GetBytes(player2);
		List<string> list = new List<string> { $"Record-boundary analysis: pid={pid}, P1='{player1}', P2='{player2}', anchors={array.Length}" };
		int[] array2 = new int[6] { 16, 32, 64, 128, 256, 512 };
		(ulong, ulong)[] array3 = array;
		for (int num = 0; num < array3.Length; num++)
		{
			(ulong, ulong) tuple = array3[num];
			try
			{
				ulong num2 = ((tuple.Item1 > 1024) ? (tuple.Item1 - 1024) : 0);
				byte[] array4 = Read(num2, 2304);
				int num3 = (int)(tuple.Item1 - num2);
				int num4 = (int)(tuple.Item2 - num2);
				bool value = num3 >= 0 && num3 + bytes.Length <= array4.Length && array4.Skip(num3).Take(bytes.Length).SequenceEqual(bytes);
				bool value2 = num4 >= 0 && num4 + bytes2.Length <= array4.Length && array4.Skip(num4).Take(bytes2.Length).SequenceEqual(bytes2);
				list.Add($"ANCHOR P1=0x{tuple.Item1:X} P2=0x{tuple.Item2:X} delta=0x{tuple.Item2 - tuple.Item1:X} exact={value}/{value2}");
				int[] array5 = array2;
				foreach (int num6 in array5)
				{
					ulong num7 = tuple.Item1 & (ulong)(~((long)num6 - 1L));
					ulong num8 = tuple.Item2 & (ulong)(~((long)num6 - 1L));
					list.Add($"  align 0x{num6:X}: P1base=0x{num7:X} off=0x{tuple.Item1 - num7:X}; P2base=0x{num8:X} off=0x{tuple.Item2 - num8:X}; baseDelta=0x{((num8 > num7) ? (num8 - num7) : (num7 - num8)):X}");
				}
				for (int num9 = -256; num9 <= 256; num9 += 64)
				{
					long num10 = (long)num3 + (long)num9;
					long num11 = (long)num4 + (long)num9;
					if (num10 < 0 || num11 < 0 || num10 + 64 > array4.Length || num11 + 64 > array4.Length)
					{
						continue;
					}
					int num12 = 0;
					for (int num13 = 0; num13 < 64; num13++)
					{
						if (array4[num10 + num13] == array4[num11 + num13])
						{
							num12++;
						}
					}
					string value3 = FindPrintableStrings(array4.Skip((int)num10).Take(64).ToArray());
					string value4 = FindPrintableStrings(array4.Skip((int)num11).Take(64).ToArray());
					list.Add($"  REL {num9:+#;-#;0}: similarity={num12}/64 strings1=[{value3}] strings2=[{value4}]");
				}
				(string, int)[] array6 = new(string, int)[2]
				{
					("P1", num3),
					("P2", num4)
				};
				for (int num5 = 0; num5 < array6.Length; num5++)
				{
					(string, int) tuple2 = array6[num5];
					string item = tuple2.Item1;
					int item2 = tuple2.Item2;
					List<string> list2 = new List<string>();
					for (int num14 = Math.Max(0, item2 - 384); num14 < Math.Min(array4.Length - 8, item2 + 384); num14++)
					{
						int num15;
						for (num15 = num14; num15 < array4.Length && array4[num15] == 0 && num15 - num14 < 128; num15++)
						{
						}
						if (num15 - num14 >= 8)
						{
							list2.Add($"0x{(ulong)((long)num2 + (long)num14):X}(rel={num14 - item2:+#;-#;0},len=0x{num15 - num14:X})");
							num14 = num15 - 1;
						}
						if (list2.Count >= 8)
						{
							break;
						}
					}
					list.Add("  " + item + " nearby zero-runs: " + ((list2.Count == 0) ? "none" : string.Join(", ", list2)));
				}
			}
			catch (Exception ex)
			{
				list.Add($"ANCHOR 0x{tuple.Item1:X}/0x{tuple.Item2:X} READ FAIL: {ex.GetBaseException().Message}");
			}
		}
		list.Add("Record-boundary analysis complete. Bounded read-only windows only; no full-memory scan and no game memory was modified.");
		return list;
		byte[] Read(ulong a, int n)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, a, n }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex2)
			{
				throw ex2.InnerException ?? ex2;
			}
		}
	}

	public List<string> Validate85Structures(object process, string player1, string player2)
	{
		int pid = ExtractPid(process);
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		(ulong, ulong)[] array = new(ulong, ulong)[5]
		{
			(44667997uL, 44668130uL),
			(47316486uL, 47316619uL),
			(47323705uL, 47323838uL),
			(47382022uL, 47382155uL),
			(47389241uL, 47389374uL)
		};
		List<string> list = new List<string> { $"0x85 structure validation: pid={pid}, P1='{player1}', P2='{player2}', pairs={array.Length}" };
		(ulong, ulong)[] array2 = array;
		for (int num = 0; num < array2.Length; num++)
		{
			(ulong, ulong) tuple = array2[num];
			try
			{
				byte[] array3 = Read(tuple.Item1 - 64, 256);
				byte[] array4 = Read(tuple.Item2 - 64, 256);
				int num2 = 0;
				int num3 = Math.Min(array3.Length, array4.Length);
				for (int num4 = 0; num4 < num3; num4++)
				{
					if (array3[num4] == array4[num4])
					{
						num2++;
					}
				}
				string text = FindPrintableStrings(array3);
				string text2 = FindPrintableStrings(array4);
				list.Add($"PAIR P1=0x{tuple.Item1:X} P2=0x{tuple.Item2:X} delta=0x{tuple.Item2 - tuple.Item1:X} byteSimilarity={num2}/{num3} ({((num3 != 0) ? (num2 * 100 / num3) : 0)}%)");
				list.Add("  P1 strings: [" + text + "]");
				list.Add("  P2 strings: [" + text2 + "]");
				List<string> list2 = new List<string>();
				for (int num5 = 0; num5 < num3; num5++)
				{
					if (list2.Count >= 24)
					{
						break;
					}
					if (array3[num5] != array4[num5])
					{
						list2.Add($"{num5 - 64:+#;-#;0}: {array3[num5]:X2}->{array4[num5]:X2}");
					}
				}
				list.Add("  first byte differences: " + ((list2.Count == 0) ? "none" : string.Join(", ", list2)));
				string text3 = FindPrintableStrings(Read(tuple.Item2, 266).Skip(133).Take(133).ToArray());
				list.Add("  next +0x85 record strings: [" + text3 + "]");
			}
			catch (Exception ex)
			{
				list.Add($"PAIR 0x{tuple.Item1:X}/0x{tuple.Item2:X} READ FAIL: {ex.GetBaseException().Message}");
			}
		}
		list.Add("0x85 structure validation complete. Fixed bounded reads only; no game memory was modified.");
		return list;
		byte[] Read(ulong a, int n)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, a, n }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex2)
			{
				throw ex2.InnerException ?? ex2;
			}
		}
	}

	public List<string> AnalyzeClientCandidateRegions(object process, string target)
	{
		int pid = ExtractPid(process);
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		ulong[] obj = new ulong[5] { 43011867uL, 44646059uL, 47338356uL, 47341944uL, 53068996uL };
		List<string> list = new List<string> { $"Client candidate analysis started: pid={pid}, target='{target}'" };
		byte[] bytes = Encoding.ASCII.GetBytes(target);
		ulong[] array = obj;
		foreach (ulong num2 in array)
		{
			try
			{
				ulong num3 = ((num2 >= 1024) ? (num2 - 1024) : 0);
				byte[] array2 = Read(num3, 2304);
				int num4 = (int)(num2 - num3);
				bool flag = num4 >= 0 && num4 + bytes.Length <= array2.Length && array2.Skip(num4).Take(bytes.Length).SequenceEqual(bytes);
				list.Add($"CANDIDATE hit=0x{num2:X} window=0x{num3:X}-0x{(ulong)((long)num3 + (long)array2.Length):X} exact={(flag ? "YES" : "NO")}");
				for (int num5 = Math.Max(0, num4 - 512); num5 < Math.Min(array2.Length, num4 + 513); num5 += 128)
				{
					int count = Math.Min(128, array2.Length - num5);
					string value = FindPrintableStrings(array2.Skip(num5).Take(count).ToArray());
					if (!string.IsNullOrWhiteSpace(value))
					{
						list.Add($"  +0x{num5 - num4:X4} @0x{(ulong)((long)num3 + (long)num5):X}: [{value}]");
					}
				}
				List<string> list2 = new List<string>();
				for (int num6 = 0; num6 + 8 <= array2.Length; num6 += 8)
				{
					ulong num7 = BitConverter.ToUInt64(array2, num6);
					ulong num8 = ((num7 > num2) ? (num7 - num2) : (num2 - num7));
					if (num7 != 0L && num8 <= 8192)
					{
						list2.Add($"@0x{(ulong)((long)num3 + (long)num6):X}->0x{num7:X}(d=0x{num8:X})");
					}
					if (list2.Count >= 12)
					{
						break;
					}
				}
				list.Add((list2.Count == 0) ? "  nearby pointer-like refs: none in local window" : ("  nearby pointer-like refs: " + string.Join(", ", list2)));
				int num9 = Math.Max(0, num4 - 64);
				int num10 = Math.Min(160, array2.Length - num9);
				list.Add($"  name context @0x{(ulong)((long)num3 + (long)num9):X}: " + BitConverter.ToString(array2, num9, num10).Replace("-", " "));
			}
			catch (Exception ex)
			{
				list.Add($"CANDIDATE 0x{num2:X} READ FAIL: {ex.GetBaseException().Message}");
			}
		}
		list.Add("Client candidate analysis complete. Read-only; no game memory was modified.");
		return list;
		byte[] Read(ulong a, int n)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, a, n }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex2)
			{
				throw ex2.InnerException ?? ex2;
			}
		}
	}

	private static string FindPrintableStrings(byte[] data)
	{
		List<string> list = new List<string>();
		int num = 0;
		while (num < data.Length)
		{
			if (data[num] < 32 || data[num] > 126)
			{
				num++;
				continue;
			}
			int i;
			for (i = num; i < data.Length && data[i] >= 32 && data[i] <= 126 && i - num < 32; i++)
			{
			}
			int num2 = i - num;
			if (num2 >= 3 && num2 <= 24 && (i == data.Length || data[i] == 0))
			{
				string text = Encoding.ASCII.GetString(data, num, num2).Trim();
				if (text.Any(char.IsLetterOrDigit) && !list.Contains<string>(text, StringComparer.OrdinalIgnoreCase))
				{
					list.Add(text);
				}
			}
			num = Math.Max(i + 1, num + 1);
		}
		return string.Join(" | ", list.Take(4));
	}

	public IEnumerable<string> DescribeApi()
	{
		yield return "Loaded API type: " + apiType.FullName;
		foreach (MethodInfo item in (from x in apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
			where x.Name.Contains("Process") || x.Name.Contains("Memory") || x.Name == "Connect"
			orderby x.Name
			select x).Take(60))
		{
			yield return "API: " + item.Name + "(" + string.Join(", ", from p in item.GetParameters()
				select p.ParameterType.Name + " " + p.Name) + ")";
		}
	}

	public List<string> DualClientPresenceMap(object process, string player1, string player2)
	{
		int pid = ExtractPid(process);
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		List<string> list = new List<string> { $"START pid={pid} P1='{player1}' P2='{(string.IsNullOrWhiteSpace(player2) ? "<none>" : player2)}' range=0x02000000-0x04000000" };
		if (string.IsNullOrWhiteSpace(player1))
		{
			list.Add("RESULT missing Player 1 name.");
			return list;
		}
		string[] array = new string[2] { player1, player2 }.Where((string x) => !string.IsNullOrWhiteSpace(x)).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		Dictionary<string, List<ulong>> dictionary = new Dictionary<string, List<ulong>>(StringComparer.OrdinalIgnoreCase);
		string[] array2 = array;
		foreach (string key in array2)
		{
			dictionary[key] = new List<ulong>();
		}
		byte[] array3 = Array.Empty<byte>();
		ulong num2 = 33554432uL;
		int num3 = 0;
		int num4 = 0;
		int num5 = array.Max((string x) => Encoding.ASCII.GetByteCount(x));
		while (num2 < 67108864)
		{
			int num6 = (int)Math.Min(262144uL, 67108864 - num2);
			byte[] array4;
			try
			{
				array4 = Read(num2, num6);
				num3++;
			}
			catch
			{
				num4++;
				num2 += (ulong)num6;
				array3 = Array.Empty<byte>();
				continue;
			}
			byte[] array5 = new byte[array3.Length + array4.Length];
			Buffer.BlockCopy(array3, 0, array5, 0, array3.Length);
			Buffer.BlockCopy(array4, 0, array5, array3.Length, array4.Length);
			ulong num7 = num2 - (ulong)array3.Length;
			array2 = array;
			foreach (string text in array2)
			{
				byte[] bytes = Encoding.ASCII.GetBytes(text);
				int num8 = 0;
				while (num8 <= array5.Length - bytes.Length)
				{
					int num9 = Find(array5.Skip(num8).ToArray(), bytes);
					if (num9 < 0)
					{
						break;
					}
					num9 += num8;
					ulong num10 = num7 + (ulong)num9;
					if (num10 >= 33554432 && num10 < 67108864 && !dictionary[text].Contains(num10) && dictionary[text].Count < 32)
					{
						dictionary[text].Add(num10);
					}
					num8 = num9 + 1;
				}
			}
			int num11 = Math.Min(Math.Max(0, num5 - 1), array5.Length);
			array3 = array5.Skip(array5.Length - num11).ToArray();
			num2 += (ulong)num6;
		}
		array2 = array;
		foreach (string text2 in array2)
		{
			list.Add($"NAME '{text2}' hits={dictionary[text2].Count}: " + ((dictionary[text2].Count == 0) ? "none" : string.Join(", ", dictionary[text2].Select((ulong value) => $"0x{value:X}"))));
			foreach (ulong item in dictionary[text2].Take(12))
			{
				try
				{
					ulong a = ((item >= 64) ? (item - 64) : 0);
					byte[] data = Read(a, 256);
					list.Add($"CTX '{text2}' hit=0x{item:X} strings=[{FindPrintableStrings(data)}]");
				}
				catch
				{
				}
			}
		}
		if (array.Length == 2)
		{
			List<string> list2 = new List<string>();
			foreach (ulong item2 in dictionary[array[0]])
			{
				foreach (ulong item3 in dictionary[array[1]])
				{
					ulong num12 = ((item2 > item3) ? (item2 - item3) : (item3 - item2));
					if (num12 <= 16384)
					{
						list2.Add($"0x{item2:X}<->0x{item3:X} d=0x{num12:X}");
					}
				}
			}
			list.Add("NEAR-PAIRS <=0x4000: " + ((list2.Count == 0) ? "none" : string.Join("; ", list2.Take(24))));
		}
		list.Add($"SUMMARY reads={num3} failures={num4}; read-only. Capture once with both players, then after P2 leaves without restarting BO2.");
		return list;
		static int Find(byte[] b, byte[] n)
		{
			if (n.Length == 0 || b.Length < n.Length)
			{
				return -1;
			}
			for (int i = 0; i <= b.Length - n.Length; i++)
			{
				bool flag = true;
				for (int j = 0; j < n.Length; j++)
				{
					if (b[i + j] != n[j])
					{
						flag = false;
						break;
					}
				}
				if (flag)
				{
					return i;
				}
			}
			return -1;
		}
		byte[] Read(ulong num13, int n)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, num13, n }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex)
			{
				throw ex.InnerException ?? ex;
			}
		}
	}

	public byte[] ReadPresenceFocusWindow(object process, ulong center, int before, int after)
	{
		int num = ExtractPid(process);
		MethodInfo methodInfo = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		ulong num2 = center - (ulong)before;
		int num3 = before + after + 1;
		try
		{
			return (byte[])(methodInfo.Invoke(api, new object[3] { num, num2, num3 }) ?? Array.Empty<byte>());
		}
		catch (TargetInvocationException ex)
		{
			throw ex.InnerException ?? ex;
		}
	}

	public List<string> PresenceCandidateProbe(object process, string player1, string player2)
	{
		int pid = ExtractPid(process);
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		List<string> list = new List<string>();
		(string, ulong)[] array = new(string, ulong)[6]
		{
			("P2_STALE_A", 44668130uL),
			("P2_STALE_B", 47316619uL),
			("P2_STALE_C", 47323838uL),
			("P2_STALE_D", 47382155uL),
			("P2_STALE_E", 47389374uL),
			("P2_LIVE_CANDIDATE", 63858577uL)
		};
		list.Add($"START pid={pid} P1='{player1}' P2='{player2}' candidates={array.Length} window=+/-0x100 read-only");
		(string, ulong)[] array2 = array;
		for (int num = 0; num < array2.Length; num++)
		{
			(string, ulong) tuple = array2[num];
			try
			{
				ulong num2 = ((tuple.Item2 >= 256) ? (tuple.Item2 - 256) : 0);
				byte[] array3 = Read(num2, 544);
				int num3 = (int)(tuple.Item2 - num2);
				string value = Hex(array3, Math.Max(0, num3 - 32), 96);
				string text = FindPrintableStrings(array3);
				bool value2 = !string.IsNullOrWhiteSpace(player1) && text.IndexOf(player1, StringComparison.OrdinalIgnoreCase) >= 0;
				bool value3 = !string.IsNullOrWhiteSpace(player2) && text.IndexOf(player2, StringComparison.OrdinalIgnoreCase) >= 0;
				list.Add($"PROBE {tuple.Item1} addr=0x{tuple.Item2:X} hash=0x{Fnv(array3):X8} hasP1={value2} hasP2={value3} center={value}");
				list.Add($"STRINGS {tuple.Item1} [{text}]");
			}
			catch (Exception ex)
			{
				list.Add($"PROBE {tuple.Item1} addr=0x{tuple.Item2:X} READ_FAIL {ex.GetBaseException().Message}");
			}
		}
		list.Add("SUMMARY compare P2_LIVE_CANDIDATE at 0x3CE6791 with P2 out vs rejoined. Stable stale controls are included for comparison.");
		list.Add("RESULT read-only candidate probe complete; no game memory modified.");
		return list;
		static uint Fnv(byte[] b)
		{
			uint num4 = 2166136261u;
			foreach (byte b2 in b)
			{
				num4 ^= b2;
				num4 *= 16777619;
			}
			return num4;
		}
		static string Hex(byte[] b, int off, int len)
		{
			return BitConverter.ToString(b, off, Math.Min(len, Math.Max(0, b.Length - off))).Replace("-", "");
		}
		byte[] Read(ulong a, int n)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, a, n }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex2)
			{
				throw ex2.InnerException ?? ex2;
			}
		}
	}

	public Dictionary<ulong, byte[]> CaptureClientMappingWatch(object process)
	{
		int pid = ExtractPid(process);
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		(ulong, int)[] obj = new(ulong, int)[3]
		{
			(21586168uL, 768),
			(63858449uL, 384),
			(17238212224uL, 768)
		};
		Dictionary<ulong, byte[]> dictionary = new Dictionary<ulong, byte[]>();
		(ulong, int)[] array = obj;
		for (int num = 0; num < array.Length; num++)
		{
			(ulong, int) tuple = array[num];
			dictionary[tuple.Item1] = Read(tuple.Item1, tuple.Item2);
		}
		return dictionary;
		byte[] Read(ulong a, int n)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, a, n }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex)
			{
				throw ex.InnerException ?? ex;
			}
		}
	}

	public List<string> CharacterizeLiveClientRecords(object process, string player1, string player2)
	{
		int pid = ExtractPid(process);
		List<string> list = new List<string>();
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		ulong start = 21569536uL;
		int n = 36864;
		byte[] b = Read(start, n);
		list.Add($"bounded region=0x{start:X}-0x{(ulong)((long)start + (long)b.Length):X} bytes={b.Length}");
		List<ulong> list2 = Hits(player1);
		List<ulong> list3 = Hits(player2);
		list.Add($"P1 exact hits={list2.Count}: [{string.Join(", ", list2.Select((ulong x) => $"0x{x:X}"))}]");
		list.Add($"P2 exact hits={list3.Count}: [{string.Join(", ", list3.Select((ulong x) => $"0x{x:X}"))}]");
		list.Add($"proven P2 name candidate=0x{21586752uL:X}; present={(list3.Contains(21586752uL) ? "YES" : "NO")}");
		ulong num = 21586752uL;
		bool flag = list2.Contains(21586424uL);
		bool flag2 = list3.Contains(num);
		list.Add($"STRIDE-CHECK P1=0x{21586424uL:X} +0x{328uL:X} => predictedP2=0x{num:X}; P1match={(flag ? "YES" : "NO")} P2match={(flag2 ? "YES" : "NO")}");
		list.Add("STRIDE-CHECK RESULT=" + ((flag && flag2) ? "PASS" : "UNVERIFIED") + " exact-name fixed-stride relationship");
		foreach (ulong item in from x in list2.Concat(list3).Distinct()
			orderby x
			select x)
		{
			ulong value = item - start;
			ulong value2 = ((item >= 584) ? (item - 584) : 0);
			list.Add($"NAME addr=0x{item:X} regionOff=0x{value:X} guessRecordStart(nameOff0x248)=0x{value2:X}");
		}
		foreach (ulong item2 in list2)
		{
			foreach (ulong item3 in list3)
			{
				long value3 = (long)(item3 - item2);
				if (Math.Abs(value3) <= 65536)
				{
					list.Add($"PAIR P1=0x{item2:X} P2=0x{item3:X} signedDelta=0x{value3:X}");
				}
			}
		}
		foreach (ulong item4 in from x in list2.Concat(list3).Distinct()
			where x >= start + 256 && x < (ulong)((long)start + (long)b.Length - 256)
			orderby x
			select x)
		{
			int num2 = (int)(item4 - start);
			string value4 = FindPrintableStrings(b.Skip(num2 - 64).Take(192).ToArray());
			list.Add($"WINDOW name=0x{item4:X} nearby=[{value4}]");
		}
		list.Add("RESULT bounded live-record characterization complete; mapping remains UNVERIFIED until P1/P2 are shown in equivalent fixed-stride records.");
		return list;
		List<ulong> Hits(string name)
		{
			List<ulong> list4 = new List<ulong>();
			if (string.IsNullOrWhiteSpace(name))
			{
				return list4;
			}
			byte[] bytes = Encoding.ASCII.GetBytes(name);
			for (int i = 0; i <= b.Length - bytes.Length; i++)
			{
				bool flag3 = true;
				for (int j = 0; j < bytes.Length; j++)
				{
					if (b[i + j] != bytes[j])
					{
						flag3 = false;
						break;
					}
				}
				if (flag3)
				{
					list4.Add(start + (ulong)i);
				}
			}
			return list4;
		}
		byte[] Read(ulong a, int num3)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, a, num3 }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex)
			{
				throw ex.InnerException ?? ex;
			}
		}
	}

	public List<string> EnumerateClientNameSlots(object process, string player1, string player2)
	{
		int pid = ExtractPid(process);
		List<string> list = new List<string>();
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		list.Add($"baseName=0x{21586424uL:X} stride=0x{328uL:X} slots={18}");
		int num = 0;
		bool flag = false;
		bool flag2 = false;
		for (int num2 = 0; num2 < 18; num2++)
		{
			ulong num3 = (ulong)(21586424 + (long)num2 * 328L);
			string text = ReadAscii(num3);
			bool flag3 = !string.IsNullOrWhiteSpace(player1) && string.Equals(text, player1, StringComparison.OrdinalIgnoreCase);
			bool flag4 = !string.IsNullOrWhiteSpace(player2) && string.Equals(text, player2, StringComparison.OrdinalIgnoreCase);
			if (!string.IsNullOrWhiteSpace(text))
			{
				num++;
			}
			if (flag3)
			{
				flag = true;
			}
			if (flag4)
			{
				flag2 = true;
			}
			list.Add($"SLOT {num2:D2} nameAddr=0x{num3:X} occupied={(string.IsNullOrWhiteSpace(text) ? "NO" : "YES")} name='{text}' match={(flag3 ? "P1" : (flag4 ? "P2" : "-"))}");
		}
		list.Add($"CROSS-CHECK P1={(flag ? "FOUND" : "NOT_FOUND")} P2={(flag2 ? "FOUND" : "NOT_FOUND")} occupiedNameSlots={num}");
		list.Add("RESULT=" + ((flag && (string.IsNullOrWhiteSpace(player2) || flag2)) ? "PASS" : "UNVERIFIED") + " 0x148 name-slot enumeration; this validates the name table only, not unrelated BO2 client/entity structures.");
		return list;
		byte[] Read(ulong a, int n)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, a, n }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex)
			{
				throw ex.InnerException ?? ex;
			}
		}
		string ReadAscii(ulong a, int max = 32)
		{
			byte[] array = Read(a, max);
			int i;
			for (i = 0; i < array.Length && array[i] >= 32 && array[i] <= 126; i++)
			{
			}
			if (i != 0)
			{
				return Encoding.ASCII.GetString(array, 0, i);
			}
			return "";
		}
	}

	public static ulong LobbyNameAddress(int slot)
	{
		if (slot < 0 || slot >= 18)
		{
			throw new ArgumentOutOfRangeException("slot", slot, $"Lobby slot must be 0..{17}.");
		}
		return (ulong)(21586424 + (long)slot * 328L);
	}

	public string ReadLobbyName(object process, int slot)
	{
		int num = ExtractPid(process);
		byte[] array = (byte[])((apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)")).Invoke(api, new object[3]
		{
			num,
			LobbyNameAddress(slot),
			32
		}) ?? Array.Empty<byte>());
		int num2;
		for (num2 = 0; num2 < array.Length && array[num2] >= 32 && array[num2] <= 126; num2++)
		{
		}
		if (num2 != 0)
		{
			return Encoding.ASCII.GetString(array, 0, num2);
		}
		return "";
	}

	public (int Slot, ulong Address) ResolveClientNameSlot(object process, string playerName)
	{
		if (string.IsNullOrWhiteSpace(playerName))
		{
			return (Slot: -1, Address: 0uL);
		}
		int num = ExtractPid(process);
		MethodInfo methodInfo = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		for (int num2 = 0; num2 < 18; num2++)
		{
			ulong num3 = (ulong)(21586424 + (long)num2 * 328L);
			byte[] array;
			try
			{
				array = (byte[])(methodInfo.Invoke(api, new object[3] { num, num3, 32 }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex)
			{
				throw ex.InnerException ?? ex;
			}
			int num4;
			for (num4 = 0; num4 < array.Length && array[num4] >= 32 && array[num4] <= 126; num4++)
			{
			}
			if (!string.Equals((num4 == 0) ? "" : Encoding.ASCII.GetString(array, 0, num4), playerName, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			ulong num5 = (ulong)(21617208 + (long)num2 * 480L);
			try
			{
				byte[] array2 = (byte[])(methodInfo.Invoke(api, new object[3]
				{
					num,
					num5 + 444,
					4
				}) ?? Array.Empty<byte>());
				if (array2.Length >= 4 && BitConverter.ToUInt32(array2, 0) != 0)
				{
					return (Slot: num2, Address: num3);
				}
			}
			catch
			{
				return (Slot: num2, Address: num3);
			}
		}
		return (Slot: -1, Address: 0uL);
	}

	public List<string> CorrelateNameSlotRecords(object process, int selectedSlot)
	{
		List<string> list = new List<string>();
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		int pid = ExtractPid(process);
		List<byte[]> records = new List<byte[]>();
		for (int num = 0; num < 18; num++)
		{
			byte[] array = Read((ulong)(21586424 + (long)num * 328L), 328);
			records.Add(array);
			string value = Name(array);
			list.Add($"SLOT {num:00} addr=0x{(ulong)(21586424 + (long)num * 328L):X} name='{value}' nonzero={array.Count((byte x) => x != 0)}{((num == selectedSlot) ? " SELECTED" : "")}");
		}
		if (selectedSlot >= 0 && selectedSlot < records.Count)
		{
			byte[] array2 = records[selectedSlot];
			List<byte[]> source = records.Where((byte[] _, int i) => i != selectedSlot && records[i].Any((byte x) => x != 0)).ToList();
			List<int> list2 = new List<int>();
			int off;
			for (off = 0; off < array2.Length; off++)
			{
				if (array2[off] != 0 && source.Any((byte[] r) => off < r.Length && r[off] != 0))
				{
					list2.Add(off);
				}
			}
			list.Add($"SELECTED slot={selectedSlot} populatedOffsetsSharedWithOtherOccupied={list2.Count}");
			if (list2.Count > 0)
			{
				list.Add("SHARED-OFFSETS " + string.Join(",", from x in list2.Take(96)
					select $"0x{x:X}") + ((list2.Count > 96) ? ",..." : ""));
			}
		}
		list.Add("RESULT name-slot record samples captured. This is correlation evidence only; it does not prove lobby/account/client-entity index equivalence.");
		return list;
		static string Name(byte[] b)
		{
			int num2 = Array.IndexOf(b, (byte)0);
			if (num2 < 0)
			{
				num2 = Math.Min(b.Length, 32);
			}
			if (num2 > 32)
			{
				num2 = 32;
			}
			return Encoding.ASCII.GetString(b, 0, num2);
		}
		byte[] Read(ulong a, int n)
		{
			return (byte[])(readMethod.Invoke(api, new object[3] { pid, a, n }) ?? Array.Empty<byte>());
		}
	}

	public byte[] ReadNameSlotPair(object process)
	{
		int num = ExtractPid(process);
		MethodInfo methodInfo = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		try
		{
			return (byte[])(methodInfo.Invoke(api, new object[3] { num, 21586424uL, 656 }) ?? Array.Empty<byte>());
		}
		catch (TargetInvocationException ex)
		{
			throw ex.InnerException ?? ex;
		}
	}

	public List<string> CompareOccupiedNameSlotFields(object process)
	{
		List<string> list = new List<string>();
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		int pid = ExtractPid(process);
		List<(int, ulong, string, byte[])> list2 = new List<(int, ulong, string, byte[])>();
		for (int num = 0; num < 18; num++)
		{
			ulong num2 = (ulong)(21586424 + (long)num * 328L);
			byte[] array = Read(num2, 328);
			string text = Name(array);
			if (!string.IsNullOrWhiteSpace(text))
			{
				list2.Add((num, num2, text, array));
			}
		}
		list.Add($"occupied={list2.Count} baseName=0x{21586424uL:X} stride=0x{328uL:X}");
		foreach (var item in list2)
		{
			list.Add($"OCCUPIED slot={item.Item1} addr=0x{item.Item2:X} name='{item.Item3}' nonzero={item.Item4.Count((byte x) => x != 0)}");
		}
		if (list2.Count < 2)
		{
			list.Add("RESULT need at least two occupied slots for field comparison.");
			return list;
		}
		(int, ulong, string, byte[]) tuple = list2[0];
		(int, ulong, string, byte[]) tuple2 = list2[1];
		List<int> list3 = new List<int>();
		List<int> list4 = new List<int>();
		List<int> list5 = new List<int>();
		for (int num3 = 0; num3 < 328; num3++)
		{
			byte b = tuple.Item4[num3];
			byte b2 = tuple2.Item4[num3];
			if (b != 0 && b2 != 0)
			{
				list3.Add(num3);
				if (b == b2)
				{
					list4.Add(num3);
				}
				else
				{
					list5.Add(num3);
				}
			}
		}
		list.Add($"PAIR slot{tuple.Item1}<->slot{tuple2.Item1} bothNonzero={list3.Count} identical={list4.Count} different={list5.Count}");
		if (list4.Count > 0)
		{
			list.Add("IDENTICAL-OFFSETS " + string.Join(",", list4.Select((int o) => $"0x{o:X}")));
		}
		if (list5.Count > 0)
		{
			list.Add("PLAYER-SPECIFIC-OFFSETS " + string.Join(",", list5.Select((int o) => $"0x{o:X}")));
		}
		foreach (int item2 in list5)
		{
			list.Add($"FIELD off=0x{item2:X3} slot{tuple.Item1}=0x{tuple.Item4[item2]:X2} slot{tuple2.Item1}=0x{tuple2.Item4[item2]:X2}");
		}
		int[] array2 = new int[24]
		{
			92, 95, 100, 112, 113, 114, 115, 116, 117, 119,
			121, 125, 127, 131, 133, 137, 139, 143, 145, 148,
			152, 184, 188, 228
		};
		foreach (int num5 in array2)
		{
			if (num5 < 328)
			{
				list.Add($"FOCUS off=0x{num5:X3} slot{tuple.Item1}=0x{tuple.Item4[num5]:X2} slot{tuple2.Item1}=0x{tuple2.Item4[num5]:X2} {((tuple.Item4[num5] == tuple2.Item4[num5]) ? "SAME" : "DIFF")}");
			}
		}
		list.Add("RESULT byte-level occupied-record comparison captured. Values are correlation evidence only; no lobby/account/client-entity equivalence is assumed.");
		return list;
		static string Name(byte[] array3)
		{
			int num6 = Array.IndexOf(array3, (byte)0);
			if (num6 < 0)
			{
				num6 = Math.Min(array3.Length, 32);
			}
			if (num6 > 32)
			{
				num6 = 32;
			}
			return Encoding.ASCII.GetString(array3, 0, num6);
		}
		byte[] Read(ulong a, int n)
		{
			return (byte[])(readMethod.Invoke(api, new object[3] { pid, a, n }) ?? Array.Empty<byte>());
		}
	}

	public List<string> CorrelateNameSlotsToClientBase(object process)
	{
		BO2ValidationResult bO2ValidationResult = ValidateBO2Specific(process);
		int pid = bO2ValidationResult.Pid;
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		int[] array = new int[11]
		{
			92, 95, 100, 115, 143, 145, 148, 152, 184, 188,
			228
		};
		List<string> list = new List<string>();
		List<(int, string, byte[])> list2 = new List<(int, string, byte[])>();
		for (int num = 0; num < 18; num++)
		{
			byte[] array2 = Read((ulong)(21586424 + (long)num * 328L), 328);
			string text = Name(array2);
			if (!string.IsNullOrWhiteSpace(text))
			{
				list2.Add((num, text, array2));
			}
		}
		list.Add($"nameSlotsOccupied={list2.Count} clientPtrAddress=0x{bO2ValidationResult.ClientBasePtrAddress:X} clientPtrValue=0x{bO2ValidationResult.ClientBasePtrValue:X}");
		foreach (var r in list2)
		{
			list.Add($"NAME slot={r.Item1} name='{r.Item2}' candidates=" + string.Join(" ", array.Select((int o) => $"+0x{o:X2}={r.Item3[o]:X2}")));
		}
		ulong num2 = bO2ValidationResult.GameBase + 27019072;
		List<(string, ulong)> list3 = new List<(string, ulong)> { ("static", num2) };
		if (bO2ValidationResult.ClientBasePtrValue >= 65536 && bO2ValidationResult.ClientBasePtrValue <= 281474976710655L && bO2ValidationResult.ClientBasePtrValue != num2)
		{
			list3.Add(("pointer", bO2ValidationResult.ClientBasePtrValue));
		}
		foreach (var item in list3)
		{
			list.Add($"BASE {item.Item1}=0x{item.Item2:X} stride=0x{872uL:X}");
			foreach (var item2 in list2)
			{
				ulong num3 = item.Item2 + (ulong)((long)item2.Item1 * 872L);
				byte[] array3 = Read(num3, 872);
				uint value = (((ulong)array3.Length >= 36uL) ? BitConverter.ToUInt32(array3, 32) : 0u);
				ulong value2 = (((ulong)array3.Length >= 352uL) ? BitConverter.ToUInt64(array3, 344) : 0);
				List<int> list4 = new List<int>();
				for (int num4 = 0; num4 + array.Length <= array3.Length; num4++)
				{
					bool flag = true;
					for (int num5 = 0; num5 < array.Length; num5++)
					{
						if (array3[num4 + num5] != item2.Item3[array[num5]])
						{
							flag = false;
							break;
						}
					}
					if (flag)
					{
						list4.Add(num4);
					}
				}
				List<string> list5 = new List<string>();
				int[] array4 = array;
				foreach (int num7 in array4)
				{
					byte b = item2.Item3[num7];
					if (b == 0)
					{
						continue;
					}
					List<int> list6 = new List<int>();
					for (int num8 = 0; num8 < array3.Length; num8++)
					{
						if (array3[num8] == b)
						{
							list6.Add(num8);
							if (list6.Count >= 8)
							{
								break;
							}
						}
					}
					list5.Add($"name+0x{num7:X2}={b:X2}->clientHits[{string.Join(",", list6.Select((int x) => $"0x{x:X}"))}{((list6.Count >= 8) ? ",..." : "")}]");
				}
				list.Add($"CLIENT {item.Item1} slot={item2.Item1} name='{item2.Item2}' addr=0x{num3:X} marker=0x{value:X8} ps=0x{value2:X} preview={Hex(array3)}");
				list.Add($"MATCH {item.Item1} slot={item2.Item1} {string.Join(" ", list5)}");
			}
		}
		list.Add("RESULT captured same-slot name-record versus BO2 client-base evidence. Matching byte values alone do not prove field identity; use marker/ps/transition consistency before enabling writes.");
		return list;
		static string Hex(byte[] source, int max = 16)
		{
			return string.Join(" ", from x in source.Take(max)
				select x.ToString("X2"));
		}
		static string Name(byte[] array5)
		{
			int i;
			for (i = 0; i < Math.Min(32, array5.Length) && array5[i] >= 32 && array5[i] <= 126; i++)
			{
			}
			if (i != 0)
			{
				return Encoding.ASCII.GetString(array5, 0, i);
			}
			return "";
		}
		byte[] Read(ulong a, int n)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, a, n }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex)
			{
				throw ex.InnerException ?? ex;
			}
		}
	}

	public List<string> FindBoundedClientBridge(object process)
	{
		BO2ValidationResult bO2ValidationResult = ValidateBO2Specific(process);
		int pid = bO2ValidationResult.Pid;
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		List<(int, string, byte)> list = new List<(int, string, byte)>();
		for (int num = 0; num < 18; num++)
		{
			byte[] array = Read((ulong)(21586424 + (long)num * 328L), 328);
			string text = Name(array);
			if (!string.IsNullOrWhiteSpace(text))
			{
				list.Add((num, text, (byte)((array.Length > 148) ? array[148] : 0)));
			}
		}
		List<string> list2 = new List<string>();
		list2.Add($"nameSlotsOccupied={list.Count} clientPtr=0x{bO2ValidationResult.ClientBasePtrValue:X}");
		if (bO2ValidationResult.ClientBasePtrValue < 65536)
		{
			list2.Add("RESULT client pointer is not plausible; bounded scan skipped.");
			return list2;
		}
		ulong num2 = ((bO2ValidationResult.ClientBasePtrValue > 65536) ? (bO2ValidationResult.ClientBasePtrValue - 65536) : 65536);
		list2.Add($"WINDOW start=0x{num2:X} end=0x{num2 + 131072:X} bytes=0x{131072uL:X} chunk=0x{16384:X}");
		foreach (var item in list)
		{
			list2.Add($"TARGET slot={item.Item1} name='{item.Item2}' id94=0x{item.Item3:X2}");
		}
		Dictionary<string, List<ulong>> dictionary = list.ToDictionary<(int, string, byte), string, List<ulong>>(((int Slot, string Name, byte Id94) x) => x.Name, ((int Slot, string Name, byte Id94) x) => new List<ulong>(), StringComparer.OrdinalIgnoreCase);
		Dictionary<string, int> dictionary2 = list.ToDictionary<(int, string, byte), string, int>(((int Slot, string Name, byte Id94) x) => x.Name, ((int Slot, string Name, byte Id94) x) => 0, StringComparer.OrdinalIgnoreCase);
		for (ulong num3 = 0uL; num3 < 131072; num3 += 16384)
		{
			int n = (int)Math.Min(16384uL, 131072 - num3);
			byte[] array2 = Read(num2 + num3, n);
			foreach (var r in list)
			{
				byte[] bytes = Encoding.ASCII.GetBytes(r.Item2 + "\0");
				foreach (int item2 in FindBytes(array2, bytes))
				{
					if (dictionary[r.Item2].Count < 16)
					{
						dictionary[r.Item2].Add(num2 + num3 + (ulong)item2);
					}
				}
				if (r.Item3 != 0)
				{
					dictionary2[r.Item2] += array2.Count((byte x) => x == r.Item3);
				}
			}
		}
		foreach (var item3 in list)
		{
			List<ulong> list3 = dictionary[item3.Item2];
			list2.Add($"NAME-HITS slot={item3.Item1} name='{item3.Item2}' count={list3.Count} [{string.Join(",", list3.Select((ulong a) => $"0x{a:X}"))}]");
			list2.Add($"ID94-SCALAR slot={item3.Item1} value=0x{item3.Item3:X2} occurrences={dictionary2[item3.Item2]} (scalar count only; not proof of identity)");
			foreach (ulong item4 in list3.Take(4))
			{
				ulong num4 = ((item4 >= 64) ? (item4 - 64) : item4);
				byte[] source = Read(num4, 256);
				list2.Add($"CONTEXT slot={item3.Item1} hit=0x{item4:X} around=0x{num4:X} nonzero={source.Count((byte x) => x != 0)}");
			}
		}
		list2.Add("RESULT bounded bridge search complete. Exact-name hits near the resolved pointer are candidates only and require leave/rejoin validation before any writes.");
		return list2;
		static List<int> FindBytes(byte[] hay, byte[] needle)
		{
			List<int> list4 = new List<int>();
			if (needle.Length == 0)
			{
				return list4;
			}
			for (int i = 0; i + needle.Length <= hay.Length; i++)
			{
				bool flag = true;
				for (int j = 0; j < needle.Length; j++)
				{
					if (hay[i + j] != needle[j])
					{
						flag = false;
						break;
					}
				}
				if (flag)
				{
					list4.Add(i);
				}
			}
			return list4;
		}
		static string Name(byte[] b)
		{
			int i;
			for (i = 0; i < Math.Min(32, b.Length) && b[i] >= 32 && b[i] <= 126; i++)
			{
			}
			if (i != 0)
			{
				return Encoding.ASCII.GetString(b, 0, i);
			}
			return "";
		}
		byte[] Read(ulong a, int num5)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, a, num5 }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex)
			{
				throw ex.InnerException ?? ex;
			}
		}
	}

	public List<string> CaptureLinkedRecordWatch(object process, string state)
	{
		int pid = ExtractPid(process);
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		List<string> lines = new List<string>();
		Dictionary<int, byte[]> dictionary = new Dictionary<int, byte[]>();
		lines.Add($"STATE={state} nameBase=0x{21586424uL:X} nameStride=0x{328uL:X} refBase=0x{21617208uL:X} refStride=0x{480uL:X}");
		for (int num = 0; num < 2; num++)
		{
			ulong num2 = (ulong)(21586424 + (long)num * 328L);
			string value = Name(Read(num2, 328));
			ulong num3 = (ulong)(21617208 + (long)num * 480L);
			byte[] array = (dictionary[num] = Read(num3, 480));
			ulong value2 = ((array.Length >= 8) ? BitConverter.ToUInt64(array, 0) : 0);
			int value3 = array.Count((byte x) => x != 0);
			lines.Add($"{state} slot={num} name='{value}' nameAddr=0x{num2:X} linkedAddr=0x{num3:X} firstQword=0x{value2:X} nonzero={value3} preview={string.Join(" ", from x in array.Take(24)
				select x.ToString("X2"))}");
			List<int> list = new List<int>();
			byte[] bytes = BitConverter.GetBytes(num2);
			for (int num4 = 0; num4 + bytes.Length <= array.Length; num4++)
			{
				bool flag = true;
				for (int num5 = 0; num5 < bytes.Length; num5++)
				{
					if (array[num4 + num5] != bytes[num5])
					{
						flag = false;
						break;
					}
				}
				if (flag)
				{
					list.Add(num4);
				}
			}
			lines.Add($"{state} slot={num} directNameRefOffsets=[{string.Join(",", list.Select((int x) => $"+0x{x:X}"))}]");
		}
		linkedRecordWatchStates[state] = dictionary;
		if (state.Equals("P2_LEFT", StringComparison.OrdinalIgnoreCase))
		{
			Diff("BOTH_IN", "P2_LEFT");
		}
		if (state.Equals("P2_REJOIN", StringComparison.OrdinalIgnoreCase))
		{
			Diff("P2_LEFT", "P2_REJOIN");
			Diff("BOTH_IN", "P2_REJOIN");
		}
		lines.Add("RESULT linked 0x1E0 record transition capture complete. Read-only; no structure equivalence assumed until transitions validate.");
		return lines;
		void Diff(string a, string b)
		{
			if (linkedRecordWatchStates.TryGetValue(a, out Dictionary<int, byte[]> value4) && linkedRecordWatchStates.TryGetValue(b, out Dictionary<int, byte[]> value5))
			{
				for (int i = 0; i < 2; i++)
				{
					byte[] x = value4[i];
					byte[] y = value5[i];
					List<string> list2 = new List<string>();
					int num6 = Math.Min(x.Length, y.Length);
					for (int j = 0; j < num6; j++)
					{
						if (x[j] != y[j] && list2.Count < 80)
						{
							list2.Add($"+0x{j:X}:{x[j]:X2}>{y[j]:X2}");
						}
					}
					int value6 = Enumerable.Range(0, num6).Count((int num7) => x[num7] != y[num7]);
					lines.Add($"DIFF {a}->{b} slot={i} changedBytes={value6} [{string.Join(",", list2)}]");
				}
			}
		}
		static string Name(byte[] b)
		{
			int i;
			for (i = 0; i < Math.Min(32, b.Length) && b[i] >= 32 && b[i] <= 126; i++)
			{
			}
			if (i != 0)
			{
				return Encoding.ASCII.GetString(b, 0, i);
			}
			return "";
		}
		byte[] Read(ulong a, int n)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, a, n }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex)
			{
				throw ex.InnerException ?? ex;
			}
		}
	}

	public List<string> CharacterizeLinkedRecordTail(object process)
	{
		int pid = ExtractPid(process);
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		List<string> list = new List<string>();
		list.Add($"BASE name=0x{21586424uL:X}/0x{328uL:X} linked=0x{21617208uL:X}/0x{480uL:X}");
		for (int num = 0; num < 2; num++)
		{
			ulong a = (ulong)(21586424 + (long)num * 328L);
			string value = Name(Read(a, 328));
			ulong num2 = (ulong)(21617208 + (long)num * 480L);
			byte[] array = Read(num2, 480);
			ulong value2 = ((array.Length >= 8) ? BitConverter.ToUInt64(array, 0) : 0);
			list.Add($"SLOT slot={num} name='{value}' linkedAddr=0x{num2:X} directNameRef=0x{value2:X} nonzero={array.Count((byte x) => x != 0)}");
			int[] array2 = new int[16]
			{
				416, 420, 424, 428, 432, 436, 440, 444, 448, 452,
				456, 460, 464, 468, 472, 476
			};
			foreach (int num4 in array2)
			{
				uint value3 = ((num4 + 4 <= array.Length) ? BitConverter.ToUInt32(array, num4) : 0u);
				list.Add($"FIELD slot={num} off=0x{num4:X3} u32=0x{value3:X8} dec={value3} bytes={string.Join(" ", from x in array.Skip(num4).Take(4)
					select x.ToString("X2"))}");
			}
			List<string> list2 = new List<string>();
			for (int num5 = 384; num5 < array.Length; num5++)
			{
				if (array[num5] != 0)
				{
					list2.Add($"+0x{num5:X}=0x{array[num5]:X2}");
				}
			}
			list.Add($"TAIL-NONZERO slot={num} count={list2.Count} [{string.Join(",", list2.Take(96))}]");
		}
		list.Add("RESULT tail fields characterized from the validated linked records. Values remain candidates; no writes or semantic assumptions made.");
		return list;
		static string Name(byte[] b)
		{
			int i;
			for (i = 0; i < Math.Min(32, b.Length) && b[i] >= 32 && b[i] <= 126; i++)
			{
			}
			if (i != 0)
			{
				return Encoding.ASCII.GetString(b, 0, i);
			}
			return "";
		}
		byte[] Read(ulong num6, int n)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, num6, n }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex)
			{
				throw ex.InnerException ?? ex;
			}
		}
	}

	public List<string> ResolveVerifiedLivePlayerNames(object process)
	{
		int pid = ExtractPid(process);
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		List<string> list = new List<string>();
		for (int num = 0; num < 18; num++)
		{
			ulong num2 = (ulong)(21586424 + (long)num * 328L);
			string text = Name(Read(num2, 328));
			if (!string.IsNullOrWhiteSpace(text))
			{
				ulong a = (ulong)(21617208 + (long)num * 480L);
				byte[] array = Read(a, 480);
				ulong num3 = ((array.Length >= 8) ? BitConverter.ToUInt64(array, 0) : 0);
				uint num4 = ((array.Length >= 448) ? BitConverter.ToUInt32(array, 444) : 0u);
				if (num3 == num2 && num4 != 0 && !list.Contains<string>(text, StringComparer.OrdinalIgnoreCase))
				{
					list.Add(text);
				}
			}
		}
		return list;
		static string Name(byte[] b)
		{
			int i;
			for (i = 0; i < Math.Min(32, b.Length) && b[i] >= 32 && b[i] <= 126; i++)
			{
			}
			if (i != 0)
			{
				return Encoding.ASCII.GetString(b, 0, i);
			}
			return "";
		}
		byte[] Read(ulong num5, int n)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, num5, n }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex)
			{
				throw ex.InnerException ?? ex;
			}
		}
	}

	public List<string> ValidateVerifiedPlayerResolver(object process, string p1, string p2)
	{
		int pid = ExtractPid(process);
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		List<string> list = new List<string>();
		List<string> list2 = new string[2]
		{
			p1?.Trim() ?? "",
			p2?.Trim() ?? ""
		}.Where((string x) => !string.IsNullOrWhiteSpace(x)).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
		list.Add($"BASE name=0x{21586424uL:X}/0x{328uL:X} linked=0x{21617208uL:X}/0x{480uL:X} expected=[{string.Join(",", list2)}]");
		Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		for (int num = 0; num < 18; num++)
		{
			ulong num2 = (ulong)(21586424 + (long)num * 328L);
			string text = Name(Read(num2, 328));
			if (!string.IsNullOrWhiteSpace(text))
			{
				dictionary[text] = num;
				ulong num3 = (ulong)(21617208 + (long)num * 480L);
				byte[] array = Read(num3, 480);
				ulong num4 = ((array.Length >= 8) ? BitConverter.ToUInt64(array, 0) : 0);
				uint num5 = ((array.Length >= 448) ? BitConverter.ToUInt32(array, 444) : 0u);
				uint value = ((array.Length >= 456) ? BitConverter.ToUInt32(array, 452) : 0u);
				uint value2 = ((array.Length >= 476) ? BitConverter.ToUInt32(array, 472) : 0u);
				bool flag = num4 == num2;
				bool flag2 = num5 != 0;
				list.Add($"CHAIN name='{text}' slot={num} nameAddr=0x{num2:X} linkedAddr=0x{num3:X} directRef=0x{num4:X} refMatch={(flag ? "YES" : "NO")} active=0x{num5:X} clientId={value} state1D8=0x{value2:X} status={((flag && flag2) ? "RESOLVED" : "UNVERIFIED")}");
			}
		}
		foreach (string item in list2)
		{
			if (dictionary.TryGetValue(item, out var value3))
			{
				list.Add($"EXPECTED name='{item}' present=YES slot={value3} result=RESOLVED");
			}
			else
			{
				list.Add("EXPECTED name='" + item + "' present=NO result=INACTIVE_OR_UNAVAILABLE");
			}
		}
		list.Add("RESULT selected-player chain validation complete. Resolver requires exact current name-slot match, direct linked-record reference, and nonzero +0x1BC active flag. Read-only; no memory modified.");
		return list;
		static string Name(byte[] b)
		{
			int i;
			for (i = 0; i < Math.Min(32, b.Length) && b[i] >= 32 && b[i] <= 126; i++)
			{
			}
			if (i != 0)
			{
				return Encoding.ASCII.GetString(b, 0, i);
			}
			return "";
		}
		byte[] Read(ulong a, int n)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, a, n }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex)
			{
				throw ex.InnerException ?? ex;
			}
		}
	}

	public List<string> TraceVerifiedNameTable(object process)
	{
		int pid = ExtractPid(process);
		MethodInfo readMethod = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		List<string> list = new List<string>();
		List<(int, ulong, string, byte[])> list2 = new List<(int, ulong, string, byte[])>();
		for (int num = 0; num < 18; num++)
		{
			ulong num2 = (ulong)(21586424 + (long)num * 328L);
			byte[] array = Read(num2, 328);
			string text = Name(array);
			if (!string.IsNullOrWhiteSpace(text))
			{
				list2.Add((num, num2, text, array));
			}
		}
		list.Add($"ANCHOR base=0x{21586424uL:X} stride=0x{328uL:X} occupied={list2.Count}");
		foreach (var item in list2)
		{
			list.Add($"OCCUPIED slot={item.Item1} addr=0x{item.Item2:X} name='{item.Item3}' id94=0x{item.Item4[148]:X2} nonzero={item.Item4.Count((byte x) => x != 0)}");
		}
		if (list2.Count == 0)
		{
			list.Add("RESULT no occupied verified name slots; refresh players and retry.");
			return list;
		}
		ulong contextStart = 21570040uL;
		byte[] array2 = Read(contextStart, 65536);
		list.Add($"CONTEXT start=0x{contextStart:X} end=0x{(ulong)((long)contextStart + (long)array2.Length):X} bytes=0x{array2.Length:X}");
		foreach (var item2 in list2)
		{
			byte[] bytes = Encoding.ASCII.GetBytes(item2.Item3 + "\\0");
			List<ulong> list3 = (from x in FindBytes(array2, bytes)
				select contextStart + (ulong)x).Take(16).ToList();
			list.Add($"LOCAL-NAME slot={item2.Item1} count={list3.Count} [{string.Join(",", list3.Select((ulong a) => $"0x{a:X}"))}]");
			byte[] bytes2 = BitConverter.GetBytes(item2.Item2);
			byte[] bytes3 = BitConverter.GetBytes((uint)item2.Item2);
			List<ulong> list4 = (from x in FindBytes(array2, bytes2)
				select contextStart + (ulong)x).Take(32).ToList();
			List<ulong> list5 = (from x in FindBytes(array2, bytes3)
				select contextStart + (ulong)x).Take(32).ToList();
			list.Add($"REF64 slot={item2.Item1} target=0x{item2.Item2:X} count={list4.Count} [{string.Join(",", list4.Select((ulong a) => $"0x{a:X}"))}]");
			list.Add($"REF32 slot={item2.Item1} target=0x{item2.Item2:X} count={list5.Count} [{string.Join(",", list5.Select((ulong a) => $"0x{a:X}"))}]");
			List<string> list6 = new List<string>();
			for (int num3 = 0; num3 + 8 <= item2.Item4.Length; num3 += 8)
			{
				ulong num4 = BitConverter.ToUInt64(item2.Item4, num3);
				if (num4 >= 4194304 && num4 <= 68719476736L)
				{
					list6.Add($"+0x{num3:X}=0x{num4:X}");
				}
			}
			list.Add($"EMBEDDED-PTR64 slot={item2.Item1} count={list6.Count} [{string.Join(",", list6.Take(24))}]");
		}
		ulong num5 = 21324280uL;
		list.Add($"REF-WINDOW start=0x{num5:X} end=0x{num5 + 524288:X} bytes=0x{524288uL:X} chunk=0x{16384:X}");
		Dictionary<int, List<ulong>> dictionary = list2.ToDictionary<(int, ulong, string, byte[]), int, List<ulong>>(((int Slot, ulong Addr, string Name, byte[] Data) r) => r.Slot, ((int Slot, ulong Addr, string Name, byte[] Data) r) => new List<ulong>());
		for (ulong num6 = 0uL; num6 < 524288; num6 += 16384)
		{
			int n = (int)Math.Min(16384uL, 524288 - num6);
			byte[] hay = Read(num5 + num6, n);
			foreach (var item3 in list2)
			{
				byte[] bytes4 = BitConverter.GetBytes(item3.Item2);
				foreach (int item4 in FindBytes(hay, bytes4))
				{
					if (dictionary[item3.Item1].Count < 32)
					{
						dictionary[item3.Item1].Add(num5 + num6 + (ulong)item4);
					}
				}
			}
		}
		foreach (var item5 in list2)
		{
			list.Add($"REF-WINDOW-HITS slot={item5.Item1} target=0x{item5.Item2:X} count={dictionary[item5.Item1].Count} [{string.Join(",", dictionary[item5.Item1].Select((ulong a) => $"0x{a:X}"))}]");
		}
		list.Add("RESULT verified name-table context/reference trace captured. References and pointer-looking values are candidates only; validate through leave/rejoin before any writes.");
		return list;
		static List<int> FindBytes(byte[] array3, byte[] needle)
		{
			List<int> list7 = new List<int>();
			if (needle.Length == 0)
			{
				return list7;
			}
			for (int i = 0; i + needle.Length <= array3.Length; i++)
			{
				bool flag = true;
				for (int j = 0; j < needle.Length; j++)
				{
					if (array3[i + j] != needle[j])
					{
						flag = false;
						break;
					}
				}
				if (flag)
				{
					list7.Add(i);
				}
			}
			return list7;
		}
		static string Name(byte[] b)
		{
			int i;
			for (i = 0; i < Math.Min(32, b.Length) && b[i] >= 32 && b[i] <= 126; i++)
			{
			}
			if (i != 0)
			{
				return Encoding.ASCII.GetString(b, 0, i);
			}
			return "";
		}
		byte[] Read(ulong a, int num7)
		{
			try
			{
				return (byte[])(readMethod.Invoke(api, new object[3] { pid, a, num7 }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex)
			{
				throw ex.InnerException ?? ex;
			}
		}
	}

	public CbufSendResult SendVerifiedCbufCommand(object process, int localClient, string command)
	{
		if (localClient < 0 || localClient > 17)
		{
			throw new ArgumentOutOfRangeException("localClient");
		}
		if (string.IsNullOrWhiteSpace(command))
		{
			throw new ArgumentException("Command is empty.", "command");
		}
		if (command.Length > 512)
		{
			throw new InvalidOperationException("Command exceeds the recovered 512-byte preview limit.");
		}
		int num = ExtractPid(process);
		ulong num2 = 7763616uL;
		byte[] array = new byte[18]
		{
			85, 72, 137, 229, 65, 86, 83, 65, 137, 254,
			191, 55, 0, 0, 0, 72, 137, 243
		};
		MethodInfo methodInfo = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid,address,length)");
		byte[] array2;
		try
		{
			array2 = (byte[])(methodInfo.Invoke(api, new object[3] { num, num2, array.Length }) ?? Array.Empty<byte>());
		}
		catch (TargetInvocationException ex)
		{
			throw ex.InnerException ?? ex;
		}
		bool flag = Enumerable.SequenceEqual(array2, array);
		if (!flag)
		{
			throw new InvalidOperationException($"Cbuf_AddText signature mismatch at 0x{num2:X}. Expected {Convert.ToHexString(array)}, got {Convert.ToHexString(array2)}.");
		}
		ulong num3 = PrepareCbufRpcSession(process);
		byte[] bytes = Encoding.ASCII.GetBytes(command + "\0");
		MethodInfo[] array3 = (from x in apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
			where x.Name == "AllocateMemory" && x.GetParameters().Length == 2
			select x).ToArray();
		if (array3.Length == 0)
		{
			throw new MissingMethodException(apiType.FullName, "AllocateMemory(pid,length)");
		}
		ulong num4 = 0uL;
		Exception innerException = null;
		MethodInfo[] array4 = array3;
		foreach (MethodInfo methodInfo2 in array4)
		{
			try
			{
				ParameterInfo[] parameters = methodInfo2.GetParameters();
				object obj = Convert.ChangeType(Math.Max(1024, bytes.Length), parameters[1].ParameterType);
				num4 = ToUInt64(methodInfo2.Invoke(api, new object[2] { num, obj }));
				if (num4 != 0L)
				{
					break;
				}
			}
			catch (Exception ex2)
			{
				innerException = ((ex2 is TargetInvocationException ex3) ? (ex3.InnerException ?? ex3) : ex2);
			}
		}
		if (num4 == 0L)
		{
			throw new InvalidOperationException("AllocateMemory failed.", innerException);
		}
		try
		{
			MethodInfo[] array5 = (from x in apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
				where x.Name == "WriteMemory" && x.GetParameters().Length == 3
				select x).ToArray();
			bool flag2 = false;
			Exception innerException2 = null;
			array4 = array5;
			foreach (MethodInfo methodInfo3 in array4)
			{
				try
				{
					ParameterInfo[] parameters2 = methodInfo3.GetParameters();
					object obj2 = ((parameters2[1].ParameterType == typeof(ulong)) ? ((object)num4) : Convert.ChangeType(num4, parameters2[1].ParameterType));
					methodInfo3.Invoke(api, new object[3] { num, obj2, bytes });
					flag2 = true;
				}
				catch (Exception ex4)
				{
					innerException2 = ((ex4 is TargetInvocationException ex5) ? (ex5.InnerException ?? ex5) : ex4);
					continue;
				}
				break;
			}
			if (!flag2)
			{
				throw new InvalidOperationException("Could not write remote command buffer.", innerException2);
			}
			byte[] array6;
			try
			{
				array6 = (byte[])(methodInfo.Invoke(api, new object[3] { num, num4, bytes.Length }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex6)
			{
				throw ex6.InnerException ?? ex6;
			}
			bool flag3 = Enumerable.SequenceEqual(array6, bytes);
			string bufferReadbackAscii = Encoding.ASCII.GetString(array6).TrimEnd('\0');
			if (!flag3)
			{
				throw new InvalidOperationException($"Remote command buffer readback mismatch at 0x{num4:X}. Expected {Convert.ToHexString(bytes)}, got {Convert.ToHexString(array6)}.");
			}
			MethodInfo[] source = (from x in apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
				where x.Name == "Call"
				select x).ToArray();
			Exception innerException3 = null;
			object v = null;
			bool flag4 = false;
			foreach (MethodInfo item in source.OrderBy((MethodInfo x) => x.GetParameters().Length))
			{
				ParameterInfo[] parameters3 = item.GetParameters();
				if (parameters3.Length == 4)
				{
					try
					{
						object[] obj3 = new object[2] { localClient, num4 };
						object obj4 = Convert.ChangeType(num, parameters3[0].ParameterType);
						object obj5 = ((parameters3[1].ParameterType == typeof(ulong)) ? ((object)num3) : Convert.ChangeType(num3, parameters3[1].ParameterType));
						object obj6 = ((parameters3[2].ParameterType == typeof(ulong)) ? ((object)num2) : Convert.ChangeType(num2, parameters3[2].ParameterType));
						object obj7 = obj3;
						v = item.Invoke(api, new object[4] { obj4, obj5, obj6, obj7 });
						flag4 = true;
					}
					catch (Exception ex7)
					{
						innerException3 = ((ex7 is TargetInvocationException ex8) ? (ex8.InnerException ?? ex8) : ex7);
						continue;
					}
					break;
				}
			}
			if (!flag4)
			{
				InvalidateCbufRpcSession();
				throw new InvalidOperationException("No compatible Call(pid,rpcstub,address,args) overload succeeded; Cbuf RPC session invalidated.", innerException3);
			}
			return new CbufSendResult(num, localClient, num2, num3, num4, flag, flag3, bufferReadbackAscii, ToUInt64(v), command);
		}
		finally
		{
			MethodInfo methodInfo4 = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "FreeMemory" && x.GetParameters().Length == 3);
			if (methodInfo4 != null)
			{
				try
				{
					ParameterInfo[] parameters4 = methodInfo4.GetParameters();
					object obj8 = ((parameters4[1].ParameterType == typeof(ulong)) ? ((object)num4) : Convert.ChangeType(num4, parameters4[1].ParameterType));
					object obj9 = Convert.ChangeType(Math.Max(1024, bytes.Length), parameters4[2].ParameterType);
					methodInfo4.Invoke(api, new object[3] { num, obj8, obj9 });
				}
				catch
				{
				}
			}
		}
	}

	public GscInjectResult InjectCompiledGsc(object process, string localGsccPath, string target = "maps/mp/gametypes/_clientids.gsc", Action<string>? stage = null)
	{
		if (!File.Exists(localGsccPath))
		{
			throw new FileNotFoundException("Compiled GSC not found.", localGsccPath);
		}
		return InjectCompiledGsc(process, File.ReadAllBytes(localGsccPath), Path.GetFileName(localGsccPath), target, stage);
	}

	public GscInjectResult InjectCompiledGsc(object process, byte[] source, string assetName, string target = "maps/mp/gametypes/_clientids.gsc", Action<string>? stage = null)
	{
		S("01 ENTER target=" + target + " asset=" + assetName);
		if (source == null || source.Length == 0)
		{
			throw new InvalidOperationException("Embedded GSC asset '" + assetName + "' is empty.");
		}
		S($"02 ASSET-READ bytes={source.Length}");
		int num = ExtractPid(process);
		S($"03 PID={num}");
		if (source.Length < 64 || source[0] != 128 || source[1] != 71)
		{
			throw new InvalidOperationException("Not a compiled BO2 GSC/GSCC (bad magic).");
		}
		ulong num2 = (ulong)((target.IndexOf("gametypes_zm", StringComparison.OrdinalIgnoreCase) >= 0) ? 1833696 : 1829248);
		ulong num3 = 4194304 + num2;
		MethodInfo methodInfo = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid,address,length)");
		S($"04 DB-PROBE addr=0x{num3:X}");
		byte[] array = (byte[])(methodInfo.Invoke(api, new object[3] { num, num3, 16 }) ?? Array.Empty<byte>());
		S($"05 DB-PROBE-READ bytes={array.Length} sig={Convert.ToHexString(array)}");
		if (array.Length < 8)
		{
			throw new InvalidOperationException($"DB_FindXAssetHeader unreadable at 0x{num3:X}.");
		}
		S("06 INSTALL-RPC START");
		ulong num4 = InvokeInstallRpc(num);
		S($"07 INSTALL-RPC PASS stub=0x{num4:X}");
		S("08 TARGET-ALLOC START");
		ulong num5 = InvokeAllocate(num, Math.Max(1024, target.Length + 1));
		S($"09 TARGET-ALLOC PASS ptr=0x{num5:X}");
		S("10 TARGET-WRITE START");
		InvokeWrite(num, num5, Encoding.ASCII.GetBytes(target + "\0"));
		S("11 TARGET-WRITE PASS");
		S("12 ASSET-LOOKUP CALL START");
		ulong num6 = InvokeCall(num, num4, num3, new object[4]
		{
			49uL,
			num5,
			1uL,
			ulong.MaxValue
		});
		S($"13 ASSET-LOOKUP PASS asset=0x{num6:X}");
		if (num6 == 0L)
		{
			throw new InvalidOperationException("GSC asset lookup returned NULL. Enter a BO2 Multiplayer match so _clientids.gsc is loaded, then try again.");
		}
		S("14 ASSET-HEADER READ START");
		byte[] array2 = (byte[])(methodInfo.Invoke(api, new object[3] { num, num6, 32 }) ?? Array.Empty<byte>());
		S($"15 ASSET-HEADER READ PASS bytes={array2.Length}");
		if (array2.Length < 32)
		{
			throw new InvalidOperationException("GSC asset header was unreadable.");
		}
		uint originalSize = BitConverter.ToUInt32(array2, 8);
		ulong num7 = BitConverter.ToUInt64(array2, 16);
		ulong originalEnd = BitConverter.ToUInt64(array2, 24);
		S($"16 STOCK-GSC READ START oldBuf=0x{num7:X}");
		byte[] array3 = (byte[])(methodInfo.Invoke(api, new object[3] { num, num7, 8 }) ?? Array.Empty<byte>());
		S("17 STOCK-GSC READ PASS magic=" + Convert.ToHexString(array3));
		if (array3.Length < 2 || array3[0] != 128 || array3[1] != 71)
		{
			throw new InvalidOperationException("Loaded _clientids asset is not a compiled GSC. Start/restart a Multiplayer match and retry.");
		}
		uint num8 = BitConverter.ToUInt32((byte[])(methodInfo.Invoke(api, new object[3]
		{
			num,
			num7 + 8,
			4
		}) ?? new byte[4]), 0);
		byte[] array4 = (byte[])source.Clone();
		Buffer.BlockCopy(BitConverter.GetBytes(num8), 0, array4, 8, 4);
		S("18 PATCH-ALLOC START");
		ulong num9 = InvokeAllocate(num, Math.Max(4096, array4.Length + 256));
		S($"19 PATCH-ALLOC PASS remote=0x{num9:X}");
		S("20 PATCH-WRITE START");
		InvokeWrite(num, num9, array4);
		S("21 PATCH-WRITE PASS");
		S("22 ASSET-POINTER WRITE START");
		InvokeWrite(num, num6 + 16, BitConverter.GetBytes(num9));
		S("23 ASSET-POINTER WRITE PASS");
		S("24 ASSET-SIZE WRITE START");
		InvokeWrite(num, num6 + 8, BitConverter.GetBytes((uint)array4.Length));
		S("25 ASSET-SIZE WRITE PASS");
		ulong num10 = checked(num9 + (ulong)array4.Length + 1);
		S($"26 ASSET-END-POINTER WRITE START end=0x{num10:X}");
		InvokeWrite(num, num6 + 24, BitConverter.GetBytes(num10));
		S("27 ASSET-END-POINTER WRITE PASS");
		uint num11 = BitConverter.ToUInt32((byte[])(methodInfo.Invoke(api, new object[3]
		{
			num,
			num6 + 8,
			4
		}) ?? new byte[4]), 0);
		ulong num12 = BitConverter.ToUInt64((byte[])(methodInfo.Invoke(api, new object[3]
		{
			num,
			num6 + 16,
			8
		}) ?? new byte[8]), 0);
		ulong num13 = BitConverter.ToUInt64((byte[])(methodInfo.Invoke(api, new object[3]
		{
			num,
			num6 + 24,
			8
		}) ?? new byte[8]), 0);
		S($"28 VERIFY size={num11} expected={array4.Length} buffer=0x{num12:X} expectedBuffer=0x{num9:X} end=0x{num13:X} expectedEnd=0x{num10:X}");
		if (num11 != (uint)array4.Length || num12 != num9 || num13 != num10)
		{
			throw new InvalidOperationException($"Injected GSC ScriptParseTree verification failed: size {num11}/{array4.Length}, buffer 0x{num12:X}/0x{num9:X}, end 0x{num13:X}/0x{num10:X}.");
		}
		S("29 COMPLETE");
		return new GscInjectResult(num, num6, num7, originalSize, originalEnd, num9, array4.Length, num8, target);
		void S(string m)
		{
			stage?.Invoke("GSC-STAGE " + m);
		}
	}

	public void RestoreCompiledGsc(object process, GscInjectResult injection, Action<string>? stage = null)
	{
		int num = ExtractPid(process);
		if (num != injection.Pid)
		{
			throw new InvalidOperationException("The attached BO2 process is different from the process where this menu was injected.");
		}
		MethodInfo methodInfo = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid, address, length)");
		byte[] obj = (byte[])(methodInfo.Invoke(api, new object[3]
		{
			num,
			injection.AssetHeader + 16,
			8
		}) ?? Array.Empty<byte>());
		if (obj.Length < 8)
		{
			throw new InvalidOperationException("Could not read the current GSC asset pointer.");
		}
		if (BitConverter.ToUInt64(obj, 0) != injection.InjectedBuffer)
		{
			throw new InvalidOperationException("GSC asset '" + injection.Target + "' no longer points to this injector's buffer; refusing to overwrite a newer change.");
		}
		S($"START target={injection.Target} asset=0x{injection.AssetHeader:X} original=0x{injection.OriginalBuffer:X}");
		InvokeWrite(num, injection.AssetHeader + 16, BitConverter.GetBytes(injection.OriginalBuffer));
		InvokeWrite(num, injection.AssetHeader + 8, BitConverter.GetBytes(injection.OriginalSize));
		InvokeWrite(num, injection.AssetHeader + 24, BitConverter.GetBytes(injection.OriginalEnd));
		byte[] array = (byte[])(methodInfo.Invoke(api, new object[3]
		{
			num,
			injection.AssetHeader + 8,
			4
		}) ?? Array.Empty<byte>());
		byte[] array2 = (byte[])(methodInfo.Invoke(api, new object[3]
		{
			num,
			injection.AssetHeader + 16,
			8
		}) ?? Array.Empty<byte>());
		byte[] array3 = (byte[])(methodInfo.Invoke(api, new object[3]
		{
			num,
			injection.AssetHeader + 24,
			8
		}) ?? Array.Empty<byte>());
		if (array.Length < 4 || array2.Length < 8 || array3.Length < 8 || BitConverter.ToUInt32(array, 0) != injection.OriginalSize || BitConverter.ToUInt64(array2, 0) != injection.OriginalBuffer || BitConverter.ToUInt64(array3, 0) != injection.OriginalEnd)
		{
			throw new InvalidOperationException("Restoring GSC asset '" + injection.Target + "' did not verify.");
		}
		S($"PASS target={injection.Target} size={injection.OriginalSize} buffer=0x{injection.OriginalBuffer:X} end=0x{injection.OriginalEnd:X}");
		void S(string message)
		{
			stage?.Invoke("GSC-RESTORE " + message);
		}
	}

	public SessionModeResult PrepareLanProfile(object process)
	{
		int num = ExtractPid(process);
		ulong num2 = 3596256uL;
		ulong num3 = 3569312uL;
		MethodInfo methodInfo = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid,address,length)");
		byte[] array = new byte[16]
		{
			85, 72, 137, 229, 65, 86, 83, 128, 61, 230,
			186, 11, 2, 1, 137, 243
		};
		byte[] array2 = new byte[18]
		{
			85, 72, 137, 229, 65, 86, 83, 65, 137, 254,
			191, 55, 0, 0, 0, 72, 137, 243
		};
		byte[] first = (byte[])(methodInfo.Invoke(api, new object[3]
		{
			num,
			4194304 + num2,
			array.Length
		}) ?? Array.Empty<byte>());
		byte[] first2 = (byte[])(methodInfo.Invoke(api, new object[3]
		{
			num,
			4194304 + num3,
			array2.Length
		}) ?? Array.Empty<byte>());
		if (!Enumerable.SequenceEqual(first, array) || !Enumerable.SequenceEqual(first2, array2))
		{
			num2 = 3597280uL;
			num3 = 3570320uL;
			byte[] array3 = new byte[16]
			{
				85, 72, 137, 229, 65, 86, 83, 128, 61, 230,
				185, 11, 2, 1, 137, 243
			};
			first = (byte[])(methodInfo.Invoke(api, new object[3]
			{
				num,
				4194304 + num2,
				array3.Length
			}) ?? Array.Empty<byte>());
			first2 = (byte[])(methodInfo.Invoke(api, new object[3]
			{
				num,
				4194304 + num3,
				array2.Length
			}) ?? Array.Empty<byte>());
			if (!Enumerable.SequenceEqual(first, array3))
			{
				throw new InvalidOperationException($"SessionMode_SetMode signature mismatch for both MP and ZM profiles (last 0x{4194304 + num2:X}).");
			}
			if (!Enumerable.SequenceEqual(first2, array2))
			{
				throw new InvalidOperationException($"Cbuf_AddText signature mismatch for both MP and ZM profiles (last 0x{4194304 + num3:X}).");
			}
		}
		ulong rpcStub = PrepareCbufRpcSession(process);
		return new SessionModeResult(num, rpcStub, 4194304 + num2, PublicApplied: false);
	}

	public SessionModeResult SetPublicMatchSession(object process)
	{
		SessionModeResult sessionModeResult = PrepareLanProfile(process);
		InvokeCall(sessionModeResult.Pid, sessionModeResult.RpcStub, sessionModeResult.SetModeAddress, new object[2] { 2uL, 1uL });
		InvokeCall(sessionModeResult.Pid, sessionModeResult.RpcStub, sessionModeResult.SetModeAddress, new object[2] { 3uL, 0uL });
		InvokeCall(sessionModeResult.Pid, sessionModeResult.RpcStub, sessionModeResult.SetModeAddress, new object[2] { 1uL, 0uL });
		return sessionModeResult with
		{
			PublicApplied = true
		};
	}

	public SessionModeResult SetLanPlayModes(object process)
	{
		SessionModeResult sessionModeResult = PrepareLanProfile(process);
		InvokeCall(sessionModeResult.Pid, sessionModeResult.RpcStub, sessionModeResult.SetModeAddress, new object[2] { 1uL, 0uL });
		InvokeCall(sessionModeResult.Pid, sessionModeResult.RpcStub, sessionModeResult.SetModeAddress, new object[2] { 2uL, 1uL });
		InvokeCall(sessionModeResult.Pid, sessionModeResult.RpcStub, 7779328uL, new object[2] { 1uL, 0uL });
		InvokeCall(sessionModeResult.Pid, sessionModeResult.RpcStub, 7779328uL, new object[2] { 5uL, 0uL });
		InvokeCall(sessionModeResult.Pid, sessionModeResult.RpcStub, 7779328uL, new object[2] { 0uL, 1uL });
		return sessionModeResult with
		{
			PublicApplied = false
		};
	}

	private ulong InvokeInstallRpc(int pid)
	{
		return ToUInt64((apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "InstallRPC" && x.GetParameters().Length == 1) ?? throw new MissingMethodException(apiType.FullName, "InstallRPC(pid)")).Invoke(api, new object[1] { pid }));
	}

	public NativeTrophyProbeResult ProbeNativeTrophy(object process, int localUser, int trophyId)
	{
		if (localUser != 0)
		{
			throw new InvalidOperationException("V6.17 trophy probe is intentionally restricted to local user 0.");
		}
		if (trophyId < 0 || trophyId > 90)
		{
			throw new ArgumentOutOfRangeException("trophyId");
		}
		int pid = ExtractPid(process);
		ulong num = 7501536uL;
		MethodInfo read = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "ReadMemory" && x.GetParameters().Length == 3) ?? throw new MissingMethodException(apiType.FullName, "ReadMemory(pid,address,length)");
		byte[] array = Convert.FromHexString("554889E541574156534883EC184C8B35044291004889F3488D4DDC498B064889");
		byte[] array2 = Read(num, array.Length);
		if (!Enumerable.SequenceEqual(array2, array))
		{
			throw new InvalidOperationException($"AwardTrophy compact-function signature mismatch at 0x{num:X}. Expected {Convert.ToHexString(array)}, got {Convert.ToHexString(array2)}.");
		}
		uint num2 = U(41191024uL + (ulong)(localUser * 4));
		uint num3 = U(41191040uL + (ulong)(localUser * 4));
		if (num2 == 0 || num2 == uint.MaxValue)
		{
			throw new InvalidOperationException($"BO2 trophy context is not initialized (0x{num2:X8}). Stay in BO2 and let the title finish NP/trophy initialization before retrying.");
		}
		if (num3 == 0 || num3 == uint.MaxValue)
		{
			throw new InvalidOperationException($"BO2 trophy handle is not initialized (0x{num3:X8}).");
		}
		ulong num4 = 41191072uL + (ulong)(localUser * 16);
		int num5 = trophyId >> 5;
		int num6 = trophyId & 0x1F;
		bool flag = (U(num4 + (ulong)(num5 * 4)) & (uint)(1 << num6)) != 0;
		if (flag)
		{
			throw new InvalidOperationException($"BO2 already marks trophy ID {trophyId} unlocked; choose a locked ID for the one-trophy probe.");
		}
		ulong num7 = InvokeAllocate(pid, 32);
		try
		{
			byte[] array3 = new byte[8];
			BitConverter.GetBytes(localUser).CopyTo(array3, 0);
			BitConverter.GetBytes(trophyId).CopyTo(array3, 4);
			InvokeWrite(pid, num7, array3);
			ulong stub = PrepareCbufRpcSession(process);
			ulong rpcReturn = InvokeCall(pid, stub, num, new object[2] { 0uL, num7 });
			Thread.Sleep(250);
			bool isUnlockedAfter = (U(num4 + (ulong)(num5 * 4)) & (uint)(1 << num6)) != 0;
			return new NativeTrophyProbeResult(pid, localUser, trophyId, num2, num3, flag, isUnlockedAfter, rpcReturn, num);
		}
		finally
		{
			MethodInfo methodInfo = apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo x) => x.Name == "FreeMemory" && x.GetParameters().Length == 3);
			if (methodInfo != null)
			{
				try
				{
					ParameterInfo[] parameters = methodInfo.GetParameters();
					object obj = ((parameters[1].ParameterType == typeof(ulong)) ? ((object)num7) : Convert.ChangeType(num7, parameters[1].ParameterType));
					object obj2 = Convert.ChangeType(32, parameters[2].ParameterType);
					methodInfo.Invoke(api, new object[3]
					{
						Convert.ChangeType(pid, parameters[0].ParameterType),
						obj,
						obj2
					});
				}
				catch
				{
				}
			}
		}
		byte[] Read(ulong a, int n)
		{
			try
			{
				return (byte[])(read.Invoke(api, new object[3] { pid, a, n }) ?? Array.Empty<byte>());
			}
			catch (TargetInvocationException ex)
			{
				throw ex.InnerException ?? ex;
			}
		}
		uint U(ulong a)
		{
			byte[] array4 = Read(a, 4);
			if (array4.Length != 4)
			{
				throw new InvalidOperationException($"Short read at 0x{a:X}.");
			}
			return BitConverter.ToUInt32(array4, 0);
		}
	}

	private ulong InvokeAllocate(int pid, int length)
	{
		Exception innerException = null;
		foreach (MethodInfo item in from x in apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
			where x.Name == "AllocateMemory" && x.GetParameters().Length == 2
			select x)
		{
			try
			{
				ParameterInfo[] parameters = item.GetParameters();
				object obj = Convert.ChangeType(length, parameters[1].ParameterType);
				ulong num = ToUInt64(item.Invoke(api, new object[2]
				{
					Convert.ChangeType(pid, parameters[0].ParameterType),
					obj
				}));
				if (num != 0L)
				{
					return num;
				}
			}
			catch (Exception ex)
			{
				innerException = ((ex is TargetInvocationException ex2) ? (ex2.InnerException ?? ex2) : ex);
			}
		}
		throw new InvalidOperationException("AllocateMemory failed.", innerException);
	}

	private void InvokeWrite(int pid, ulong address, byte[] data)
	{
		Exception innerException = null;
		foreach (MethodInfo item in from x in apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
			where x.Name == "WriteMemory" && x.GetParameters().Length == 3
			select x)
		{
			try
			{
				ParameterInfo[] parameters = item.GetParameters();
				object obj = ((parameters[1].ParameterType == typeof(ulong)) ? ((object)address) : Convert.ChangeType(address, parameters[1].ParameterType));
				item.Invoke(api, new object[3]
				{
					Convert.ChangeType(pid, parameters[0].ParameterType),
					obj,
					data
				});
				return;
			}
			catch (Exception ex)
			{
				innerException = ((ex is TargetInvocationException ex2) ? (ex2.InnerException ?? ex2) : ex);
			}
		}
		throw new InvalidOperationException("WriteMemory failed.", innerException);
	}

	private ulong InvokeCall(int pid, ulong stub, ulong fn, object[] args)
	{
		Exception innerException = null;
		foreach (MethodInfo item in from x in apiType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
			where x.Name == "Call"
			orderby x.GetParameters().Length
			select x)
		{
			if (item.GetParameters().Length == 4)
			{
				try
				{
					ParameterInfo[] parameters = item.GetParameters();
					object obj = Convert.ChangeType(pid, parameters[0].ParameterType);
					object obj2 = ((parameters[1].ParameterType == typeof(ulong)) ? ((object)stub) : Convert.ChangeType(stub, parameters[1].ParameterType));
					object obj3 = ((parameters[2].ParameterType == typeof(ulong)) ? ((object)fn) : Convert.ChangeType(fn, parameters[2].ParameterType));
					return ToUInt64(item.Invoke(api, new object[4] { obj, obj2, obj3, args }));
				}
				catch (Exception ex)
				{
					innerException = ((ex is TargetInvocationException ex2) ? (ex2.InnerException ?? ex2) : ex);
				}
			}
		}
		throw new InvalidOperationException("No compatible RPC Call overload succeeded.", innerException);
	}
}
internal sealed record GscInjectResult(int Pid, ulong AssetHeader, ulong OriginalBuffer, uint OriginalSize, ulong OriginalEnd, ulong InjectedBuffer, int Size, uint StockChecksum, string Target);
internal sealed record ProcessSearchResult(object? Process, string DisplayName, List<string> Diagnostics);
internal sealed record MemoryReadResult(int Pid, ulong Address, int Requested, int Returned, string Preview);
internal sealed record BO2ValidationResult(int Pid, ulong DbAddress, ulong GameBase, ulong ClientBasePtrAddress, ulong ClientBasePtrValue);
internal sealed record BO2ClientRead(int Slot, ulong ClientAddress, ulong PlayerState, uint Marker, uint Rank, uint Prestige, string CandidateStrings);
internal sealed record CbufSendResult(int Pid, int LocalClient, ulong CbufAddress, ulong RpcStub, ulong RemoteCommandBuffer, bool SignatureOk, bool BufferReadbackOk, string BufferReadbackAscii, ulong ReturnValue, string Command);
internal sealed record SessionModeResult(int Pid, ulong RpcStub, ulong SetModeAddress, bool PublicApplied);
internal sealed record NativeTrophyProbeResult(int Pid, int LocalUser, int TrophyId, uint Context, uint Handle, bool WasUnlocked, bool IsUnlockedAfter, ulong RpcReturn, ulong FunctionAddress);
public sealed class MainForm : Form
{
	private sealed record WeaponChoice(int Id, string Name)
	{
		public override string ToString()
		{
			return $"{Name} ({Id})";
		}
	}

	private sealed record ModMenuAsset(string Target, string Path);

	private sealed record ModMenuPackage(string Name, List<ModMenuAsset> Assets)
	{
		public override string ToString()
		{
			return Name;
		}
	}

	private sealed record AttachSnapshot(DebugBridge Bridge, object? Process, string DisplayName, List<string> LogLines);

	private sealed class PremiumButton : Button
	{
		private bool _hover;

		private bool _down;

		public PremiumButton()
		{
			base.FlatStyle = FlatStyle.Flat;
			base.FlatAppearance.BorderSize = 0;
			Font = new Font("Segoe UI", 9f, FontStyle.Bold);
			Cursor = Cursors.Hand;
			BackColor = CleanSurface;
			ForeColor = CleanText;
			base.UseVisualStyleBackColor = false;
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		}

		protected override void OnMouseEnter(EventArgs e)
		{
			base.OnMouseEnter(e);
			if (base.Enabled)
			{
				_hover = true;
				Invalidate();
			}
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			base.OnMouseLeave(e);
			_hover = false;
			_down = false;
			Invalidate();
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			if (base.Enabled)
			{
				_down = true;
				PlayClickSound();
				Invalidate();
			}
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			base.OnMouseUp(e);
			_down = false;
			Invalidate();
		}

		protected override void OnEnabledChanged(EventArgs e)
		{
			base.OnEnabledChanged(e);
			Invalidate();
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
			Rectangle rectangle = new Rectangle(0, 0, base.Width - 1, base.Height - 1);
			int radius = 6;
			Color color = (base.Enabled ? CleanAccent : CleanBorder);
			Color color2 = ((!base.Enabled) ? Color.FromArgb(20, 20, 25) : (_down ? Color.FromArgb(38, 38, 48) : (_hover ? Color.FromArgb(32, 32, 42) : CleanSurface)));
			Color color3 = ((!base.Enabled) ? CleanTextMuted : ((_hover || _down) ? Color.FromArgb(250, 250, 255) : CleanText));
			using (GraphicsPath path = Round(rectangle, radius))
			{
				using SolidBrush brush = new SolidBrush(color2);
				graphics.FillPath(brush, path);
				Color baseColor = ((!base.Enabled) ? Color.FromArgb(38, 38, 48) : ((_hover || _down) ? color : CleanBorder));
				using Pen pen = new Pen(Color.FromArgb((!base.Enabled) ? 100 : ((_hover || _down) ? 220 : 130), baseColor), (_hover || _down) ? 1.4f : 1f);
				graphics.DrawPath(pen, path);
			}
			using SolidBrush brush2 = new SolidBrush(color3);
			graphics.DrawString(Text, Font, brush2, rectangle, FmtCenterNW);
		}

		private static GraphicsPath Round(Rectangle r, int radius)
		{
			GraphicsPath graphicsPath = new GraphicsPath();
			int num = radius * 2;
			graphicsPath.AddArc(r.X, r.Y, num, num, 180f, 90f);
			graphicsPath.AddArc(r.Right - num, r.Y, num, num, 270f, 90f);
			graphicsPath.AddArc(r.Right - num, r.Bottom - num, num, num, 0f, 90f);
			graphicsPath.AddArc(r.X, r.Bottom - num, num, num, 90f, 90f);
			graphicsPath.CloseFigure();
			return graphicsPath;
		}
	}

	private sealed class InfoBar : Control
	{
		private readonly string _text;

		public InfoBar(string text)
		{
			_text = text;
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			Font = new Font("Segoe UI", 9f);
			base.Height = 46;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
			Rectangle r = new Rectangle(0, 0, base.Width - 1, base.Height - 1);
			int num = 6;
			using (GraphicsPath path = Round2(r, num))
			{
				using SolidBrush brush = new SolidBrush(Color.FromArgb(24, 26, 34));
				graphics.FillPath(brush, path);
				using GraphicsPath graphicsPath = new GraphicsPath();
				graphicsPath.AddArc(r.X, r.Y, num * 2, num * 2, 180f, 90f);
				graphicsPath.AddArc(r.X, r.Bottom - num * 2, num * 2, num * 2, 90f, 90f);
				graphicsPath.CloseFigure();
				using SolidBrush brush2 = new SolidBrush(CleanAccent);
				graphics.FillPath(brush2, graphicsPath);
				using Pen pen = new Pen(Color.FromArgb(90, CleanBorder), 1f);
				graphics.DrawPath(pen, path);
			}
			using SolidBrush brush3 = new SolidBrush(Color.FromArgb(178, 180, 192));
			RectangleF layoutRectangle = new RectangleF(16f, 6f, base.Width - 30, base.Height - 12);
			graphics.DrawString(_text, FontBody9, brush3, layoutRectangle, FmtLeftTrim);
		}

		private static GraphicsPath Round2(Rectangle r, int radius)
		{
			GraphicsPath graphicsPath = new GraphicsPath();
			int num = radius * 2;
			graphicsPath.AddArc(r.X, r.Y, num, num, 180f, 90f);
			graphicsPath.AddArc(r.Right - num, r.Y, num, num, 270f, 90f);
			graphicsPath.AddArc(r.Right - num, r.Bottom - num, num, num, 0f, 90f);
			graphicsPath.AddArc(r.X, r.Bottom - num, num, num, 90f, 90f);
			graphicsPath.CloseFigure();
			return graphicsPath;
		}
	}

	public static class Motion
	{
		public static float OutCubic(float t)
		{
			return 1f - (float)Math.Pow(1f - Math.Clamp(t, 0f, 1f), 3.0);
		}

		public static float InOutQuad(float t)
		{
			t = Math.Clamp(t, 0f, 1f);
			if (!(t < 0.5f))
			{
				return 1f - (float)Math.Pow(-2f * t + 2f, 2.0) / 2f;
			}
			return 2f * t * t;
		}

		public static float Lerp(float current, float target, float speed)
		{
			return current + (target - current) * Math.Clamp(speed, 0.01f, 1f);
		}

		public static bool Step(ref float current, float target, float dt, float halfLife = 0.055f)
		{
			float value = 1f - (float)Math.Pow(0.5, dt / Math.Max(0.001f, halfLife));
			current += (target - current) * Math.Clamp(value, 0f, 1f);
			return Math.Abs(target - current) > 0.004f;
		}
	}

	private static class UiSound
	{
		private static SoundPlayer? _click;

		private static readonly object Gate = new object();

		private static float[] clickWave => SoftClick();

		public static void Init()
		{
			if (_click != null)
			{
				return;
			}
			lock (Gate)
			{
				if (_click != null)
				{
					return;
				}
				try
				{
					_click = new SoundPlayer(BuildWav(clickWave));
					_click.Load();
				}
				catch (Exception)
				{
					_click = null;
				}
			}
		}

		private static float[] SoftClick()
		{
			int num = 793;
			float[] array = new float[num];
			uint num2 = 5351363u;
			for (int i = 0; i < num; i++)
			{
				num2 ^= num2 << 13;
				num2 ^= num2 >> 17;
				num2 ^= num2 << 5;
				double num3 = (double)(num2 & 0xFFFF) / 32767.5 - 1.0;
				double num4 = (double)i / 44100.0;
				double num5 = Math.Exp((0.0 - num4) / 0.0045) * Math.Min(1.0, num4 / 0.001);
				array[i] = (float)(num3 * num5 * 0.075);
			}
			return array;
		}

		private static MemoryStream BuildWav(float[] samples)
		{
			int v = 88200;
			int num = 2;
			int num2 = samples.Length * num;
			MemoryStream memoryStream = new MemoryStream();
			BinaryWriter w = new BinaryWriter(memoryStream, Encoding.UTF8, leaveOpen: true);
			try
			{
				Tag("RIFF");
				U(36 + num2);
				Tag("WAVE");
				Tag("fmt ");
				U(16);
				U16(1);
				U16(1);
				U(44100);
				U(v);
				U16(num);
				U16(16);
				Tag("data");
				U(num2);
				foreach (float value in samples)
				{
					w.Write(BitConverter.GetBytes((short)(Math.Clamp(value, -1f, 1f) * 32767f)));
				}
				w.Flush();
				memoryStream.Position = 0L;
				return memoryStream;
			}
			finally
			{
				if (w != null)
				{
					((IDisposable)w).Dispose();
				}
			}
			void Tag(string s)
			{
				foreach (char c in s)
				{
					w.Write((byte)c);
				}
			}
			void U(int value2)
			{
				w.Write(BitConverter.GetBytes(value2));
			}
			void U16(int num3)
			{
				w.Write(BitConverter.GetBytes((short)num3));
			}
		}

		public static void Click()
		{
			Play(_click);
		}

		private static void Play(SoundPlayer? player)
		{
			if (player == null)
			{
				return;
			}
			Task.Run(delegate
			{
				try
				{
					lock (player)
					{
						player.Stop();
						player.PlaySync();
					}
				}
				catch (Exception)
				{
				}
			});
		}
	}

	internal static class Notify
	{
		private static NotifyIcon? _tray;

		private static readonly object Gate = new object();

		public static void Alert(string title, string message)
		{
			try
			{
				ShowBalloon(title, message);
			}
			catch
			{
			}
		}

		public static void ShowBalloon(string title, string message)
		{
			lock (Gate)
			{
				try
				{
					if (_tray == null)
					{
						_tray = new NotifyIcon
						{
							Icon = (Program.LoadAppIcon() ?? SystemIcons.Application),
							Text = "BO2 TOOL @wyzyxc",
							Visible = true
						};
					}
					_tray.BalloonTipTitle = title;
					_tray.BalloonTipText = message;
					_tray.BalloonTipIcon = ToolTipIcon.Info;
					_tray.ShowBalloonTip(6000);
				}
				catch
				{
				}
			}
		}

		public static void PostToUi(Action action)
		{
			try
			{
				Form activeForm = Form.ActiveForm;
				if (activeForm != null && activeForm.IsHandleCreated)
				{
					activeForm.BeginInvoke(action);
				}
				else
				{
					action();
				}
			}
			catch
			{
				try
				{
					action();
				}
				catch
				{
				}
			}
		}
	}

	private sealed class CleanModeButton : Button
	{
		private readonly bool _active;

		private readonly bool _locked;

		private float _glow;

		private float _targetGlow;

		private bool _hover;

		private int _pressOffset;

		private static readonly Color ActiveBg = Color.FromArgb(30, 41, 59);

		private static readonly Color ActiveBorder = Color.FromArgb(96, 165, 250);

		private static readonly Color ActiveFg = Color.FromArgb(240, 248, 255);

		private static readonly Color InactiveBg = Color.FromArgb(22, 22, 28);

		private static readonly Color InactiveFg = Color.FromArgb(105, 105, 125);

		private static readonly Color LockedBg = Color.FromArgb(20, 20, 25);

		private static readonly Color LockedFg = Color.FromArgb(80, 80, 98);

		public CleanModeButton(string text, bool active, bool locked = false)
		{
			_active = active;
			_locked = locked;
			_glow = (active ? 1f : 0f);
			_targetGlow = _glow;
			Text = text;
			base.FlatStyle = FlatStyle.Flat;
			base.FlatAppearance.BorderSize = 0;
			Font = new Font("Segoe UI", 12.5f, FontStyle.Bold);
			Cursor = (locked ? Cursors.No : Cursors.Hand);
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			if (locked && !active)
			{
				BackColor = LockedBg;
				ForeColor = LockedFg;
			}
			else
			{
				BackColor = (active ? ActiveBg : InactiveBg);
				ForeColor = (active ? ActiveFg : InactiveFg);
			}
			Animate();
		}

		private void Animate()
		{
			System.Windows.Forms.Timer t = new System.Windows.Forms.Timer
			{
				Interval = 16
			};
			long last = Environment.TickCount64;
			t.Tick += delegate
			{
				long tickCount = Environment.TickCount64;
				float dt = (float)(tickCount - last) / 1000f;
				last = tickCount;
				if (!Motion.Step(ref _glow, _targetGlow, dt, 0.06f))
				{
					_glow = _targetGlow;
					t.Stop();
					t.Dispose();
				}
				Invalidate();
			};
			t.Start();
		}

		protected override void OnMouseEnter(EventArgs e)
		{
			base.OnMouseEnter(e);
			if (!_locked || _active)
			{
				_hover = true;
				_targetGlow = Math.Min(1f, _glow + 0.45f);
				if (!_active)
				{
					BackColor = CleanSurfaceHover;
				}
			}
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			base.OnMouseLeave(e);
			_hover = false;
			_targetGlow = (_active ? 1f : 0f);
			if (!_active)
			{
				BackColor = InactiveBg;
			}
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			if (!_locked || _active)
			{
				PlayClickSound();
				_pressOffset = 1;
				Invalidate();
			}
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			base.OnMouseUp(e);
			_pressOffset = 0;
			Invalidate();
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
			Rectangle r = new Rectangle(0, 0, base.Width - 1, base.Height - 1);
			int num = 8;
			if (_glow > 0.02f && (!_locked || _active))
			{
				for (int num2 = 3; num2 >= 1; num2--)
				{
					int num3 = num2 * 2;
					using Pen pen = new Pen(Color.FromArgb((int)(40f * _glow), ActiveBorder), 2f);
					graphics.DrawRectangle(pen, 1 - num3 / 2, 1 - num3 / 2, r.Width + num3, r.Height + num3);
				}
			}
			using (GraphicsPath path = Rounded(r, num))
			{
				using SolidBrush brush = new SolidBrush(_active ? ActiveBg : (_hover ? CleanSurfaceHover : (_locked ? LockedBg : InactiveBg)));
				graphics.FillPath(brush, path);
				Color baseColor = (_active ? ActiveBorder : (_hover ? CleanBorderBright : CleanBorder));
				using Pen pen2 = new Pen(Color.FromArgb(_active ? 200 : (_hover ? 170 : 120), baseColor), _active ? 1.6f : 1f);
				graphics.DrawPath(pen2, path);
				if (_active)
				{
					using SolidBrush brush2 = new SolidBrush(ActiveBorder);
					graphics.FillRectangle(brush2, num, r.Bottom - 2, r.Width - num * 2, 2);
				}
			}
			using SolidBrush brush3 = new SolidBrush(_active ? ActiveFg : ((_locked && !_active) ? LockedFg : (_hover ? CleanText : InactiveFg)));
			RectangleF layoutRectangle = new RectangleF(0f, _pressOffset, r.Width, r.Height);
			graphics.DrawString(Text, Font, brush3, layoutRectangle, FmtCenterNW);
		}

		private static GraphicsPath Rounded(Rectangle r, int radius)
		{
			GraphicsPath graphicsPath = new GraphicsPath();
			int num = radius * 2;
			graphicsPath.AddArc(r.X, r.Y, num, num, 180f, 90f);
			graphicsPath.AddArc(r.Right - num, r.Y, num, num, 270f, 90f);
			graphicsPath.AddArc(r.Right - num, r.Bottom - num, num, num, 0f, 90f);
			graphicsPath.AddArc(r.X, r.Bottom - num, num, num, 90f, 90f);
			graphicsPath.CloseFigure();
			return graphicsPath;
		}
	}

	private sealed class CleanStatusStripRenderer : ToolStripProfessionalRenderer
	{
		private sealed class CleanColorTable : ProfessionalColorTable
		{
			public override Color ToolStripGradientBegin => CleanSurface;

			public override Color ToolStripGradientMiddle => CleanSurface;

			public override Color ToolStripGradientEnd => CleanSurface;

			public override Color MenuItemSelected => CleanSurfaceHover;

			public override Color MenuItemSelectedGradientBegin => CleanSurfaceHover;

			public override Color MenuItemSelectedGradientEnd => CleanSurfaceHover;

			public override Color MenuItemBorder => CleanBorder;

			public override Color ButtonSelectedHighlight => CleanAccent;

			public override Color ButtonSelectedBorder => CleanAccent;

			public override Color StatusStripGradientBegin => CleanSurface;

			public override Color StatusStripGradientEnd => CleanSurface;
		}

		public CleanStatusStripRenderer()
			: base(new CleanColorTable())
		{
			base.RoundedEdges = false;
		}

		protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
		{
			using SolidBrush brush = new SolidBrush(CleanSurface);
			e.Graphics.FillRectangle(brush, e.AffectedBounds);
		}

		protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
		{
			using Pen pen = new Pen(CleanBorder, 1f);
			Rectangle affectedBounds = e.AffectedBounds;
			e.Graphics.DrawLine(pen, affectedBounds.Left, affectedBounds.Top, affectedBounds.Right, affectedBounds.Top);
		}
	}

	private sealed class NavBar : Control
	{
		private int _hover = -1;

		private int _selected;

		private readonly List<string> _labels = new List<string>();

		private readonly List<Rectangle> _rects = new List<Rectangle>();

		public static readonly Color BarBg = Color.FromArgb(31, 34, 45);

		public static readonly Color PillSel = Color.FromArgb(48, 66, 98);

		public static readonly Color PillHover = Color.FromArgb(46, 50, 64);

		public static readonly Color PillIdle = Color.FromArgb(28, 30, 40);

		public static readonly Color FgSel = Color.FromArgb(248, 251, 255);

		public static readonly Color FgIdle = Color.FromArgb(200, 205, 218);

		public static readonly Color Stroke = Color.FromArgb(92, 99, 118);

		public static readonly Color StrokeSel = Color.FromArgb(120, 176, 255);

		private static readonly StringFormat Center = new StringFormat
		{
			Alignment = StringAlignment.Center,
			LineAlignment = StringAlignment.Center,
			FormatFlags = (StringFormatFlags.NoWrap | StringFormatFlags.NoClip)
		};

		private static readonly Font FontIdle = new Font("Segoe UI", 9.5f);

		private static readonly Font FontSel = new Font("Segoe UI", 9.5f, FontStyle.Bold);

		public event Action<int>? CategoryClicked;

		public NavBar()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			BackColor = BarBg;
			Cursor = Cursors.Hand;
		}

		public void AddCategory(string label)
		{
			_labels.Add(label);
			LayoutItems();
		}

		public void SetSelected(int index)
		{
			if (index >= 0 && index < _labels.Count && _selected != index)
			{
				_selected = index;
				Invalidate();
			}
		}

		public void LayoutItems()
		{
			_rects.Clear();
			int count = _labels.Count;
			if (count == 0)
			{
				return;
			}
			int num = base.ClientSize.Width - 16;
			if (num > 0)
			{
				int num2 = Math.Max(74, Math.Min(160, (num - 6 * (count - 1)) / count));
				int num3 = Math.Max(22, Math.Min(30, base.Height - 10));
				for (int i = 0; i < count; i++)
				{
					_rects.Add(new Rectangle(8 + i * (num2 + 6), (base.Height - num3) / 2, num2, num3));
				}
				Invalidate();
			}
		}

		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);
			LayoutItems();
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			int num = _rects.FindIndex((Rectangle r) => r.Contains(e.Location));
			if (num != _hover)
			{
				_hover = num;
				Invalidate();
			}
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			base.OnMouseLeave(e);
			if (_hover != -1)
			{
				_hover = -1;
				Invalidate();
			}
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			int num = _rects.FindIndex((Rectangle r) => r.Contains(e.Location));
			if (num >= 0)
			{
				Focus();
				PlayClickSound();
				this.CategoryClicked?.Invoke(num);
			}
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
			using (SolidBrush brush = new SolidBrush(BarBg))
			{
				graphics.FillRectangle(brush, new Rectangle(0, 0, base.Width, base.Height));
			}
			using SolidBrush solidBrush = new SolidBrush(PillSel);
			using SolidBrush solidBrush2 = new SolidBrush(PillHover);
			using SolidBrush solidBrush3 = new SolidBrush(PillIdle);
			using Pen pen = new Pen(StrokeSel, 1.3f);
			using Pen pen2 = new Pen(Stroke, 1f);
			using SolidBrush solidBrush4 = new SolidBrush(FgSel);
			using SolidBrush solidBrush5 = new SolidBrush(FgIdle);
			using SolidBrush brush2 = new SolidBrush(CleanAccent);
			for (int i = 0; i < _rects.Count; i++)
			{
				Rectangle rectangle = _rects[i];
				bool flag = i == _selected;
				bool flag2 = i == _hover;
				using (GraphicsPath path = RoundPath(rectangle, 6))
				{
					graphics.FillPath(flag ? solidBrush : (flag2 ? solidBrush2 : solidBrush3), path);
					graphics.DrawPath(flag ? pen : pen2, path);
				}
				if (flag)
				{
					graphics.FillRectangle(brush2, rectangle.X + 10, rectangle.Y, rectangle.Width - 20, 2);
				}
				graphics.DrawString(_labels[i], flag ? FontSel : FontIdle, flag ? solidBrush4 : solidBrush5, rectangle, Center);
			}
			using Pen pen3 = new Pen(Color.FromArgb(70, 76, 94), 1f);
			graphics.DrawLine(pen3, 0, base.Height - 1, base.Width, base.Height - 1);
		}

		private static GraphicsPath RoundPath(Rectangle r, int radius)
		{
			GraphicsPath graphicsPath = new GraphicsPath();
			int num = radius * 2;
			graphicsPath.AddArc(r.X, r.Y, num, num, 180f, 90f);
			graphicsPath.AddArc(r.Right - num, r.Y, num, num, 270f, 90f);
			graphicsPath.AddArc(r.Right - num, r.Bottom - num, num, num, 0f, 90f);
			graphicsPath.AddArc(r.X, r.Bottom - num, num, num, 90f, 90f);
			graphicsPath.CloseFigure();
			return graphicsPath;
		}
	}

	internal static readonly bool StandaloneGscOptionsUpdating = true;

	private readonly bool zombiesMode;

	private readonly TextBox ipBox = new TextBox
	{
		Text = "192.168.1.100",
		Width = 150
	};

	private readonly Label ps4Status = StatusLabel("Disconnected");

	private readonly Label bo2Status = StatusLabel("Not attached");

	private readonly Label gameState = StatusLabel("Not checked");

	private readonly Label clientsStatus = StatusLabel("0");

	private readonly Label gscStatus = StatusLabel("Idle");

	private readonly List<GscInjectResult> injectedModMenuAssets = new List<GscInjectResult>();

	private string? injectedModMenuName;

	private readonly ListBox players = new ListBox
	{
		Dock = DockStyle.Fill
	};

	private readonly TextBox log = new TextBox
	{
		Multiline = true,
		ReadOnly = true,
		ScrollBars = ScrollBars.Vertical,
		Dock = DockStyle.Fill
	};

	private readonly System.Windows.Forms.Timer monitor = new System.Windows.Forms.Timer
	{
		Interval = 1500
	};

	private readonly ToolStripStatusLabel modificationStatus = new ToolStripStatusLabel
	{
		Text = "Ready | Unsaved modifications: 0"
	};

	private readonly ToolStripProgressBar modificationProgress = new ToolStripProgressBar
	{
		Minimum = 0,
		Maximum = 100,
		Value = 0,
		Width = 260
	};

	private int unsavedModificationCount;

	private TcpClient? debugProbe;

	private DebugBridge? debugBridge;

	private object? bo2Process;

	private Button? attachButton;

	private Button? updatesButton;

	private bool playersRefreshInProgress;

	private const ulong WallhackToggleAddress = 4196770uL;

	private readonly NumericUpDown snapshotSlot = new NumericUpDown
	{
		Minimum = 0m,
		Maximum = 17m,
		Width = 65,
		Value = 1m
	};

	private byte[]? snapshotBaseline;

	private int snapshotBaselineSlot = -1;

	private byte[]? expandedBaseline;

	private int expandedBaselineSlot = -1;

	private ulong expandedBaselineAddress;

	private const int ExpandedBefore = 2048;

	private const int ExpandedAfter = 2048;

	private List<DebugBridge.RegionSnapshot>? scanBaseline;

	private List<DebugBridge.RegionSnapshot>? abaA1;

	private List<DebugBridge.RegionSnapshot>? abaB;

	private List<DebugBridge.RegionSnapshot>? abaA2;

	private readonly System.Windows.Forms.Timer validatorTimer = new System.Windows.Forms.Timer
	{
		Interval = 500
	};

	private bool validatorRunning;

	private Dictionary<ulong, byte>? validatorLast;

	private byte? controlledOriginal;

	private ulong? controlledSavedAddress;

	private readonly ComboBox controlledCandidate = new ComboBox
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Width = 145
	};

	private readonly System.Windows.Forms.Timer transitionTimer = new System.Windows.Forms.Timer
	{
		Interval = 100
	};

	private Dictionary<ulong, byte>? transitionBaseline;

	private Dictionary<ulong, byte>? transitionLast;

	private DateTime transitionStarted;

	private bool transitionRunning;

	private byte[]? slotFieldBothIn;

	private byte[]? slotFieldP2Left;

	private byte[]? slotFieldP2Rejoin;

	private string? selectedLivePlayerName;

	private bool rebuildingLivePlayerList;

	private readonly Label selectedPlayerDetails = new Label
	{
		AutoSize = true,
		MaximumSize = new Size(480, 0),
		Text = "Selected: <none> | Active: NO | Name Slot: - | Name Address: -"
	};

	private byte[]? presenceFocusBaseline;

	private const ulong PresenceFocusAddress = 63858577uL;

	private const int PresenceFocusBefore = 1024;

	private const int PresenceFocusAfter = 1024;

	private readonly System.Windows.Forms.Timer lanCaptureTimer = new System.Windows.Forms.Timer
	{
		Interval = 1000
	};

	private bool lanCaptureBusy;

	private string? lanCaptureFolder;

	private int lanCaptureIndex;

	private DateTime lanCaptureStarted;

	private List<DebugBridge.RegionSnapshot>? lanPreviousRegions;

	private byte[]? lanPreviousRecords;

	private static readonly int[] Bo2LevelXp = new int[56]
	{
		0, 0, 800, 1900, 3300, 5300, 7900, 11100, 14900, 19300,
		24300, 30100, 36700, 44100, 52300, 61300, 71100, 81700, 93100, 105300,
		118300, 132100, 146700, 162100, 178300, 195300, 213100, 231700, 251100, 271300,
		292300, 314100, 337100, 361300, 386700, 413300, 441100, 470100, 500300, 531700,
		564300, 598100, 633100, 669300, 706700, 745300, 785100, 826100, 868300, 911700,
		958300, 1002100, 1049100, 1097300, 1146700, 1197300
	};

	private static readonly string[] RecoveryCommands = new string[2387]
	{
		"statWriteDDL emblemstats backgrounds 0 purchased 1", "statWriteDDL emblemstats backgrounds 0 unlocked 1", "statWriteDDL emblemstats backgrounds 1 purchased 1", "statWriteDDL emblemstats backgrounds 1 unlocked 1", "statWriteDDL emblemstats backgrounds 2 purchased 1", "statWriteDDL emblemstats backgrounds 2 unlocked 1", "statWriteDDL emblemstats backgrounds 3 purchased 1", "statWriteDDL emblemstats backgrounds 3 unlocked 1", "statWriteDDL emblemstats backgrounds 4 purchased 1", "statWriteDDL emblemstats backgrounds 4 unlocked 1",
		"statWriteDDL emblemstats backgrounds 5 purchased 1", "statWriteDDL emblemstats backgrounds 5 unlocked 1", "statWriteDDL emblemstats backgrounds 6 purchased 1", "statWriteDDL emblemstats backgrounds 6 unlocked 1", "statWriteDDL emblemstats backgrounds 7 purchased 1", "statWriteDDL emblemstats backgrounds 7 unlocked 1", "statWriteDDL emblemstats backgrounds 8 purchased 1", "statWriteDDL emblemstats backgrounds 8 unlocked 1", "statWriteDDL emblemstats backgrounds 9 purchased 1", "statWriteDDL emblemstats backgrounds 9 unlocked 1",
		"statWriteDDL emblemstats backgrounds 10 purchased 1", "statWriteDDL emblemstats backgrounds 10 unlocked 1", "statWriteDDL emblemstats backgrounds 11 purchased 1", "statWriteDDL emblemstats backgrounds 11 unlocked 1", "statWriteDDL emblemstats backgrounds 12 purchased 1", "statWriteDDL emblemstats backgrounds 12 unlocked 1", "statWriteDDL emblemstats backgrounds 13 purchased 1", "statWriteDDL emblemstats backgrounds 13 unlocked 1", "statWriteDDL emblemstats backgrounds 14 purchased 1", "statWriteDDL emblemstats backgrounds 14 unlocked 1",
		"statWriteDDL emblemstats backgrounds 15 purchased 1", "statWriteDDL emblemstats backgrounds 15 unlocked 1", "statWriteDDL emblemstats backgrounds 16 purchased 1", "statWriteDDL emblemstats backgrounds 16 unlocked 1", "statWriteDDL emblemstats backgrounds 17 purchased 1", "statWriteDDL emblemstats backgrounds 17 unlocked 1", "statWriteDDL emblemstats backgrounds 18 purchased 1", "statWriteDDL emblemstats backgrounds 18 unlocked 1", "statWriteDDL emblemstats backgrounds 19 purchased 1", "statWriteDDL emblemstats backgrounds 19 unlocked 1",
		"statWriteDDL emblemstats backgrounds 20 purchased 1", "statWriteDDL emblemstats backgrounds 20 unlocked 1", "statWriteDDL emblemstats backgrounds 21 purchased 1", "statWriteDDL emblemstats backgrounds 21 unlocked 1", "statWriteDDL emblemstats backgrounds 22 purchased 1", "statWriteDDL emblemstats backgrounds 22 unlocked 1", "statWriteDDL emblemstats backgrounds 23 purchased 1", "statWriteDDL emblemstats backgrounds 23 unlocked 1", "statWriteDDL emblemstats backgrounds 24 purchased 1", "statWriteDDL emblemstats backgrounds 24 unlocked 1",
		"statWriteDDL emblemstats backgrounds 25 purchased 1", "statWriteDDL emblemstats backgrounds 25 unlocked 1", "statWriteDDL emblemstats backgrounds 26 purchased 1", "statWriteDDL emblemstats backgrounds 26 unlocked 1", "statWriteDDL emblemstats backgrounds 27 purchased 1", "statWriteDDL emblemstats backgrounds 27 unlocked 1", "statWriteDDL emblemstats backgrounds 28 purchased 1", "statWriteDDL emblemstats backgrounds 28 unlocked 1", "statWriteDDL emblemstats backgrounds 29 purchased 1", "statWriteDDL emblemstats backgrounds 29 unlocked 1",
		"statWriteDDL emblemstats backgrounds 30 purchased 1", "statWriteDDL emblemstats backgrounds 30 unlocked 1", "statWriteDDL emblemstats backgrounds 31 purchased 1", "statWriteDDL emblemstats backgrounds 31 unlocked 1", "statWriteDDL emblemstats backgrounds 32 purchased 1", "statWriteDDL emblemstats backgrounds 32 unlocked 1", "statWriteDDL emblemstats backgrounds 33 purchased 1", "statWriteDDL emblemstats backgrounds 33 unlocked 1", "statWriteDDL emblemstats backgrounds 34 purchased 1", "statWriteDDL emblemstats backgrounds 34 unlocked 1",
		"statWriteDDL emblemstats backgrounds 35 purchased 1", "statWriteDDL emblemstats backgrounds 35 unlocked 1", "statWriteDDL emblemstats backgrounds 36 purchased 1", "statWriteDDL emblemstats backgrounds 36 unlocked 1", "statWriteDDL emblemstats backgrounds 37 purchased 1", "statWriteDDL emblemstats backgrounds 37 unlocked 1", "statWriteDDL emblemstats backgrounds 38 purchased 1", "statWriteDDL emblemstats backgrounds 38 unlocked 1", "statWriteDDL emblemstats backgrounds 39 purchased 1", "statWriteDDL emblemstats backgrounds 39 unlocked 1",
		"statWriteDDL emblemstats backgrounds 40 purchased 1", "statWriteDDL emblemstats backgrounds 40 unlocked 1", "statWriteDDL emblemstats backgrounds 41 purchased 1", "statWriteDDL emblemstats backgrounds 41 unlocked 1", "statWriteDDL emblemstats backgrounds 42 purchased 1", "statWriteDDL emblemstats backgrounds 42 unlocked 1", "statWriteDDL emblemstats backgrounds 43 purchased 1", "statWriteDDL emblemstats backgrounds 43 unlocked 1", "statWriteDDL emblemstats backgrounds 44 purchased 1", "statWriteDDL emblemstats backgrounds 44 unlocked 1",
		"statWriteDDL emblemstats backgrounds 45 purchased 1", "statWriteDDL emblemstats backgrounds 45 unlocked 1", "statWriteDDL emblemstats backgrounds 46 purchased 1", "statWriteDDL emblemstats backgrounds 46 unlocked 1", "statWriteDDL emblemstats backgrounds 47 purchased 1", "statWriteDDL emblemstats backgrounds 47 unlocked 1", "statWriteDDL emblemstats backgrounds 48 purchased 1", "statWriteDDL emblemstats backgrounds 48 unlocked 1", "statWriteDDL emblemstats backgrounds 49 purchased 1", "statWriteDDL emblemstats backgrounds 49 unlocked 1",
		"statWriteDDL emblemstats backgrounds 50 purchased 1", "statWriteDDL emblemstats backgrounds 50 unlocked 1", "statWriteDDL emblemstats backgrounds 51 purchased 1", "statWriteDDL emblemstats backgrounds 51 unlocked 1", "statWriteDDL emblemstats backgrounds 52 purchased 1", "statWriteDDL emblemstats backgrounds 52 unlocked 1", "statWriteDDL emblemstats backgrounds 53 purchased 1", "statWriteDDL emblemstats backgrounds 53 unlocked 1", "statWriteDDL emblemstats backgrounds 54 purchased 1", "statWriteDDL emblemstats backgrounds 54 unlocked 1",
		"statWriteDDL emblemstats backgrounds 55 purchased 1", "statWriteDDL emblemstats backgrounds 55 unlocked 1", "statWriteDDL emblemstats backgrounds 56 purchased 1", "statWriteDDL emblemstats backgrounds 56 unlocked 1", "statWriteDDL emblemstats backgrounds 57 purchased 1", "statWriteDDL emblemstats backgrounds 57 unlocked 1", "statWriteDDL emblemstats backgrounds 58 purchased 1", "statWriteDDL emblemstats backgrounds 58 unlocked 1", "statWriteDDL emblemstats backgrounds 59 purchased 1", "statWriteDDL emblemstats backgrounds 59 unlocked 1",
		"statWriteDDL emblemstats backgrounds 60 purchased 1", "statWriteDDL emblemstats backgrounds 60 unlocked 1", "statWriteDDL emblemstats backgrounds 61 purchased 1", "statWriteDDL emblemstats backgrounds 61 unlocked 1", "statWriteDDL emblemstats backgrounds 62 purchased 1", "statWriteDDL emblemstats backgrounds 62 unlocked 1", "statWriteDDL emblemstats backgrounds 63 purchased 1", "statWriteDDL emblemstats backgrounds 63 unlocked 1", "statWriteDDL emblemstats backgrounds 64 purchased 1", "statWriteDDL emblemstats backgrounds 64 unlocked 1",
		"statWriteDDL emblemstats backgrounds 65 purchased 1", "statWriteDDL emblemstats backgrounds 65 unlocked 1", "statWriteDDL emblemstats backgrounds 66 purchased 1", "statWriteDDL emblemstats backgrounds 66 unlocked 1", "statWriteDDL emblemstats backgrounds 67 purchased 1", "statWriteDDL emblemstats backgrounds 67 unlocked 1", "statWriteDDL emblemstats backgrounds 68 purchased 1", "statWriteDDL emblemstats backgrounds 68 unlocked 1", "statWriteDDL emblemstats backgrounds 69 purchased 1", "statWriteDDL emblemstats backgrounds 69 unlocked 1",
		"statWriteDDL emblemstats backgrounds 70 purchased 1", "statWriteDDL emblemstats backgrounds 70 unlocked 1", "statWriteDDL emblemstats backgrounds 71 purchased 1", "statWriteDDL emblemstats backgrounds 71 unlocked 1", "statWriteDDL emblemstats backgrounds 72 purchased 1", "statWriteDDL emblemstats backgrounds 72 unlocked 1", "statWriteDDL emblemstats backgrounds 73 purchased 1", "statWriteDDL emblemstats backgrounds 73 unlocked 1", "statWriteDDL emblemstats backgrounds 74 purchased 1", "statWriteDDL emblemstats backgrounds 74 unlocked 1",
		"statWriteDDL emblemstats backgrounds 75 purchased 1", "statWriteDDL emblemstats backgrounds 75 unlocked 1", "statWriteDDL emblemstats backgrounds 76 purchased 1", "statWriteDDL emblemstats backgrounds 76 unlocked 1", "statWriteDDL emblemstats backgrounds 77 purchased 1", "statWriteDDL emblemstats backgrounds 77 unlocked 1", "statWriteDDL emblemstats backgrounds 78 purchased 1", "statWriteDDL emblemstats backgrounds 78 unlocked 1", "statWriteDDL emblemstats backgrounds 79 purchased 1", "statWriteDDL emblemstats backgrounds 79 unlocked 1",
		"statWriteDDL emblemstats backgrounds 80 purchased 1", "statWriteDDL emblemstats backgrounds 80 unlocked 1", "statWriteDDL emblemstats backgrounds 81 purchased 1", "statWriteDDL emblemstats backgrounds 81 unlocked 1", "statWriteDDL emblemstats backgrounds 82 purchased 1", "statWriteDDL emblemstats backgrounds 82 unlocked 1", "statWriteDDL emblemstats backgrounds 83 purchased 1", "statWriteDDL emblemstats backgrounds 83 unlocked 1", "statWriteDDL emblemstats backgrounds 84 purchased 1", "statWriteDDL emblemstats backgrounds 84 unlocked 1",
		"statWriteDDL emblemstats backgrounds 85 purchased 1", "statWriteDDL emblemstats backgrounds 85 unlocked 1", "statWriteDDL emblemstats backgrounds 86 purchased 1", "statWriteDDL emblemstats backgrounds 86 unlocked 1", "statWriteDDL emblemstats backgrounds 87 purchased 1", "statWriteDDL emblemstats backgrounds 87 unlocked 1", "statWriteDDL emblemstats backgrounds 88 purchased 1", "statWriteDDL emblemstats backgrounds 88 unlocked 1", "statWriteDDL emblemstats backgrounds 89 purchased 1", "statWriteDDL emblemstats backgrounds 89 unlocked 1",
		"statWriteDDL emblemstats backgrounds 90 purchased 1", "statWriteDDL emblemstats backgrounds 90 unlocked 1", "statWriteDDL emblemstats backgrounds 91 purchased 1", "statWriteDDL emblemstats backgrounds 91 unlocked 1", "statWriteDDL emblemstats backgrounds 92 purchased 1", "statWriteDDL emblemstats backgrounds 92 unlocked 1", "statWriteDDL emblemstats backgrounds 93 purchased 1", "statWriteDDL emblemstats backgrounds 93 unlocked 1", "statWriteDDL emblemstats backgrounds 94 purchased 1", "statWriteDDL emblemstats backgrounds 94 unlocked 1",
		"statWriteDDL emblemstats backgrounds 95 purchased 1", "statWriteDDL emblemstats backgrounds 95 unlocked 1", "statWriteDDL emblemstats backgrounds 96 purchased 1", "statWriteDDL emblemstats backgrounds 96 unlocked 1", "statWriteDDL emblemstats backgrounds 97 purchased 1", "statWriteDDL emblemstats backgrounds 97 unlocked 1", "statWriteDDL emblemstats backgrounds 98 purchased 1", "statWriteDDL emblemstats backgrounds 98 unlocked 1", "statWriteDDL emblemstats backgrounds 99 purchased 1", "statWriteDDL emblemstats backgrounds 99 unlocked 1",
		"statWriteDDL emblemstats backgrounds 100 purchased 1", "statWriteDDL emblemstats backgrounds 100 unlocked 1", "statWriteDDL emblemstats backgrounds 101 purchased 1", "statWriteDDL emblemstats backgrounds 101 unlocked 1", "statWriteDDL emblemstats backgrounds 102 purchased 1", "statWriteDDL emblemstats backgrounds 102 unlocked 1", "statWriteDDL emblemstats backgrounds 103 purchased 1", "statWriteDDL emblemstats backgrounds 103 unlocked 1", "statWriteDDL emblemstats backgrounds 104 purchased 1", "statWriteDDL emblemstats backgrounds 104 unlocked 1",
		"statWriteDDL emblemstats backgrounds 105 purchased 1", "statWriteDDL emblemstats backgrounds 105 unlocked 1", "statWriteDDL emblemstats backgrounds 106 purchased 1", "statWriteDDL emblemstats backgrounds 106 unlocked 1", "statWriteDDL emblemstats backgrounds 107 purchased 1", "statWriteDDL emblemstats backgrounds 107 unlocked 1", "statWriteDDL emblemstats backgrounds 108 purchased 1", "statWriteDDL emblemstats backgrounds 108 unlocked 1", "statWriteDDL emblemstats backgrounds 109 purchased 1", "statWriteDDL emblemstats backgrounds 109 unlocked 1",
		"statWriteDDL emblemstats backgrounds 110 purchased 1", "statWriteDDL emblemstats backgrounds 110 unlocked 1", "statWriteDDL emblemstats backgrounds 111 purchased 1", "statWriteDDL emblemstats backgrounds 111 unlocked 1", "statWriteDDL emblemstats backgrounds 112 purchased 1", "statWriteDDL emblemstats backgrounds 112 unlocked 1", "statWriteDDL emblemstats backgrounds 113 purchased 1", "statWriteDDL emblemstats backgrounds 113 unlocked 1", "statWriteDDL emblemstats backgrounds 114 purchased 1", "statWriteDDL emblemstats backgrounds 114 unlocked 1",
		"statWriteDDL emblemstats backgrounds 115 purchased 1", "statWriteDDL emblemstats backgrounds 115 unlocked 1", "statWriteDDL emblemstats backgrounds 116 purchased 1", "statWriteDDL emblemstats backgrounds 116 unlocked 1", "statWriteDDL emblemstats backgrounds 117 purchased 1", "statWriteDDL emblemstats backgrounds 117 unlocked 1", "statWriteDDL emblemstats backgrounds 118 purchased 1", "statWriteDDL emblemstats backgrounds 118 unlocked 1", "statWriteDDL emblemstats backgrounds 119 purchased 1", "statWriteDDL emblemstats backgrounds 119 unlocked 1",
		"statWriteDDL emblemstats backgrounds 120 purchased 1", "statWriteDDL emblemstats backgrounds 120 unlocked 1", "statWriteDDL emblemstats backgrounds 121 purchased 1", "statWriteDDL emblemstats backgrounds 121 unlocked 1", "statWriteDDL emblemstats backgrounds 122 purchased 1", "statWriteDDL emblemstats backgrounds 122 unlocked 1", "statWriteDDL emblemstats backgrounds 123 purchased 1", "statWriteDDL emblemstats backgrounds 123 unlocked 1", "statWriteDDL emblemstats backgrounds 124 purchased 1", "statWriteDDL emblemstats backgrounds 124 unlocked 1",
		"statWriteDDL emblemstats backgrounds 125 purchased 1", "statWriteDDL emblemstats backgrounds 125 unlocked 1", "statWriteDDL emblemstats backgrounds 126 purchased 1", "statWriteDDL emblemstats backgrounds 126 unlocked 1", "statWriteDDL emblemstats backgrounds 127 purchased 1", "statWriteDDL emblemstats backgrounds 127 unlocked 1", "statWriteDDL emblemstats backgrounds 128 purchased 1", "statWriteDDL emblemstats backgrounds 128 unlocked 1", "statWriteDDL emblemstats backgrounds 129 purchased 1", "statWriteDDL emblemstats backgrounds 129 unlocked 1",
		"statWriteDDL emblemstats backgrounds 130 purchased 1", "statWriteDDL emblemstats backgrounds 130 unlocked 1", "statWriteDDL emblemstats backgrounds 131 purchased 1", "statWriteDDL emblemstats backgrounds 131 unlocked 1", "statWriteDDL emblemstats backgrounds 132 purchased 1", "statWriteDDL emblemstats backgrounds 132 unlocked 1", "statWriteDDL emblemstats backgrounds 133 purchased 1", "statWriteDDL emblemstats backgrounds 133 unlocked 1", "statWriteDDL emblemstats backgrounds 134 purchased 1", "statWriteDDL emblemstats backgrounds 134 unlocked 1",
		"statWriteDDL emblemstats backgrounds 135 purchased 1", "statWriteDDL emblemstats backgrounds 135 unlocked 1", "statWriteDDL emblemstats backgrounds 136 purchased 1", "statWriteDDL emblemstats backgrounds 136 unlocked 1", "statWriteDDL emblemstats backgrounds 137 purchased 1", "statWriteDDL emblemstats backgrounds 137 unlocked 1", "statWriteDDL emblemstats backgrounds 138 purchased 1", "statWriteDDL emblemstats backgrounds 138 unlocked 1", "statWriteDDL emblemstats backgrounds 139 purchased 1", "statWriteDDL emblemstats backgrounds 139 unlocked 1",
		"statWriteDDL emblemstats backgrounds 140 purchased 1", "statWriteDDL emblemstats backgrounds 140 unlocked 1", "statWriteDDL emblemstats backgrounds 141 purchased 1", "statWriteDDL emblemstats backgrounds 141 unlocked 1", "statWriteDDL emblemstats backgrounds 142 purchased 1", "statWriteDDL emblemstats backgrounds 142 unlocked 1", "statWriteDDL emblemstats backgrounds 143 purchased 1", "statWriteDDL emblemstats backgrounds 143 unlocked 1", "statWriteDDL emblemstats backgrounds 144 purchased 1", "statWriteDDL emblemstats backgrounds 144 unlocked 1",
		"statWriteDDL emblemstats backgrounds 145 purchased 1", "statWriteDDL emblemstats backgrounds 145 unlocked 1", "statWriteDDL emblemstats backgrounds 146 purchased 1", "statWriteDDL emblemstats backgrounds 146 unlocked 1", "statWriteDDL emblemstats backgrounds 147 purchased 1", "statWriteDDL emblemstats backgrounds 147 unlocked 1", "statWriteDDL emblemstats backgrounds 148 purchased 1", "statWriteDDL emblemstats backgrounds 148 unlocked 1", "statWriteDDL emblemstats backgrounds 149 purchased 1", "statWriteDDL emblemstats backgrounds 149 unlocked 1",
		"statWriteDDL emblemstats backgrounds 150 purchased 1", "statWriteDDL emblemstats backgrounds 150 unlocked 1", "statWriteDDL emblemstats backgrounds 151 purchased 1", "statWriteDDL emblemstats backgrounds 151 unlocked 1", "statWriteDDL emblemstats backgrounds 152 purchased 1", "statWriteDDL emblemstats backgrounds 152 unlocked 1", "statWriteDDL emblemstats backgrounds 153 purchased 1", "statWriteDDL emblemstats backgrounds 153 unlocked 1", "statWriteDDL emblemstats backgrounds 154 purchased 1", "statWriteDDL emblemstats backgrounds 154 unlocked 1",
		"statWriteDDL emblemstats backgrounds 155 purchased 1", "statWriteDDL emblemstats backgrounds 155 unlocked 1", "statWriteDDL emblemstats backgrounds 156 purchased 1", "statWriteDDL emblemstats backgrounds 156 unlocked 1", "statWriteDDL emblemstats backgrounds 157 purchased 1", "statWriteDDL emblemstats backgrounds 157 unlocked 1", "statWriteDDL emblemstats backgrounds 158 purchased 1", "statWriteDDL emblemstats backgrounds 158 unlocked 1", "statWriteDDL emblemstats backgrounds 159 purchased 1", "statWriteDDL emblemstats backgrounds 159 unlocked 1",
		"statWriteDDL emblemstats backgrounds 160 purchased 1", "statWriteDDL emblemstats backgrounds 160 unlocked 1", "statWriteDDL emblemstats backgrounds 161 purchased 1", "statWriteDDL emblemstats backgrounds 161 unlocked 1", "statWriteDDL emblemstats backgrounds 162 purchased 1", "statWriteDDL emblemstats backgrounds 162 unlocked 1", "statWriteDDL emblemstats backgrounds 163 purchased 1", "statWriteDDL emblemstats backgrounds 163 unlocked 1", "statWriteDDL emblemstats backgrounds 164 purchased 1", "statWriteDDL emblemstats backgrounds 164 unlocked 1",
		"statWriteDDL emblemstats backgrounds 165 purchased 1", "statWriteDDL emblemstats backgrounds 165 unlocked 1", "statWriteDDL emblemstats backgrounds 166 purchased 1", "statWriteDDL emblemstats backgrounds 166 unlocked 1", "statWriteDDL emblemstats backgrounds 167 purchased 1", "statWriteDDL emblemstats backgrounds 167 unlocked 1", "statWriteDDL emblemstats backgrounds 168 purchased 1", "statWriteDDL emblemstats backgrounds 168 unlocked 1", "statWriteDDL emblemstats backgrounds 169 purchased 1", "statWriteDDL emblemstats backgrounds 169 unlocked 1",
		"statWriteDDL emblemstats backgrounds 170 purchased 1", "statWriteDDL emblemstats backgrounds 170 unlocked 1", "statWriteDDL emblemstats backgrounds 171 purchased 1", "statWriteDDL emblemstats backgrounds 171 unlocked 1", "statWriteDDL emblemstats backgrounds 172 purchased 1", "statWriteDDL emblemstats backgrounds 172 unlocked 1", "statWriteDDL emblemstats backgrounds 173 purchased 1", "statWriteDDL emblemstats backgrounds 173 unlocked 1", "statWriteDDL emblemstats backgrounds 174 purchased 1", "statWriteDDL emblemstats backgrounds 174 unlocked 1",
		"statWriteDDL emblemstats backgrounds 175 purchased 1", "statWriteDDL emblemstats backgrounds 175 unlocked 1", "statWriteDDL emblemstats backgrounds 176 purchased 1", "statWriteDDL emblemstats backgrounds 176 unlocked 1", "statWriteDDL emblemstats backgrounds 177 purchased 1", "statWriteDDL emblemstats backgrounds 177 unlocked 1", "statWriteDDL emblemstats backgrounds 178 purchased 1", "statWriteDDL emblemstats backgrounds 178 unlocked 1", "statWriteDDL emblemstats backgrounds 179 purchased 1", "statWriteDDL emblemstats backgrounds 179 unlocked 1",
		"statWriteDDL emblemstats backgrounds 180 purchased 1", "statWriteDDL emblemstats backgrounds 180 unlocked 1", "statWriteDDL emblemstats backgrounds 181 purchased 1", "statWriteDDL emblemstats backgrounds 181 unlocked 1", "statWriteDDL emblemstats backgrounds 182 purchased 1", "statWriteDDL emblemstats backgrounds 182 unlocked 1", "statWriteDDL emblemstats backgrounds 183 purchased 1", "statWriteDDL emblemstats backgrounds 183 unlocked 1", "statWriteDDL emblemstats backgrounds 184 purchased 1", "statWriteDDL emblemstats backgrounds 184 unlocked 1",
		"statWriteDDL emblemstats backgrounds 185 purchased 1", "statWriteDDL emblemstats backgrounds 185 unlocked 1", "statWriteDDL emblemstats backgrounds 186 purchased 1", "statWriteDDL emblemstats backgrounds 186 unlocked 1", "statWriteDDL emblemstats backgrounds 187 purchased 1", "statWriteDDL emblemstats backgrounds 187 unlocked 1", "statWriteDDL emblemstats backgrounds 188 purchased 1", "statWriteDDL emblemstats backgrounds 188 unlocked 1", "statWriteDDL emblemstats backgrounds 189 purchased 1", "statWriteDDL emblemstats backgrounds 189 unlocked 1",
		"statWriteDDL emblemstats backgrounds 190 purchased 1", "statWriteDDL emblemstats backgrounds 190 unlocked 1", "statWriteDDL emblemstats backgrounds 191 purchased 1", "statWriteDDL emblemstats backgrounds 191 unlocked 1", "statWriteDDL emblemstats backgrounds 192 purchased 1", "statWriteDDL emblemstats backgrounds 192 unlocked 1", "statWriteDDL emblemstats backgrounds 193 purchased 1", "statWriteDDL emblemstats backgrounds 193 unlocked 1", "statWriteDDL emblemstats backgrounds 194 purchased 1", "statWriteDDL emblemstats backgrounds 194 unlocked 1",
		"statWriteDDL emblemstats backgrounds 195 purchased 1", "statWriteDDL emblemstats backgrounds 195 unlocked 1", "statWriteDDL emblemstats backgrounds 196 purchased 1", "statWriteDDL emblemstats backgrounds 196 unlocked 1", "statWriteDDL emblemstats backgrounds 197 purchased 1", "statWriteDDL emblemstats backgrounds 197 unlocked 1", "statWriteDDL emblemstats backgrounds 198 purchased 1", "statWriteDDL emblemstats backgrounds 198 unlocked 1", "statWriteDDL emblemstats backgrounds 199 purchased 1", "statWriteDDL emblemstats backgrounds 199 unlocked 1",
		"statWriteDDL emblemstats backgrounds 200 purchased 1", "statWriteDDL emblemstats backgrounds 200 unlocked 1", "statWriteDDL emblemstats backgrounds 201 purchased 1", "statWriteDDL emblemstats backgrounds 201 unlocked 1", "statWriteDDL emblemstats backgrounds 202 purchased 1", "statWriteDDL emblemstats backgrounds 202 unlocked 1", "statWriteDDL emblemstats backgrounds 203 purchased 1", "statWriteDDL emblemstats backgrounds 203 unlocked 1", "statWriteDDL emblemstats backgrounds 204 purchased 1", "statWriteDDL emblemstats backgrounds 204 unlocked 1",
		"statWriteDDL emblemstats backgrounds 205 purchased 1", "statWriteDDL emblemstats backgrounds 205 unlocked 1", "statWriteDDL emblemstats backgrounds 206 purchased 1", "statWriteDDL emblemstats backgrounds 206 unlocked 1", "statWriteDDL emblemstats backgrounds 207 purchased 1", "statWriteDDL emblemstats backgrounds 207 unlocked 1", "statWriteDDL emblemstats backgrounds 208 purchased 1", "statWriteDDL emblemstats backgrounds 208 unlocked 1", "statWriteDDL emblemstats backgrounds 209 purchased 1", "statWriteDDL emblemstats backgrounds 209 unlocked 1",
		"statWriteDDL emblemstats backgrounds 210 purchased 1", "statWriteDDL emblemstats backgrounds 210 unlocked 1", "statWriteDDL emblemstats backgrounds 211 purchased 1", "statWriteDDL emblemstats backgrounds 211 unlocked 1", "statWriteDDL emblemstats backgrounds 212 purchased 1", "statWriteDDL emblemstats backgrounds 212 unlocked 1", "statWriteDDL emblemstats backgrounds 213 purchased 1", "statWriteDDL emblemstats backgrounds 213 unlocked 1", "statWriteDDL emblemstats backgrounds 214 purchased 1", "statWriteDDL emblemstats backgrounds 214 unlocked 1",
		"statWriteDDL emblemstats backgrounds 215 purchased 1", "statWriteDDL emblemstats backgrounds 215 unlocked 1", "statWriteDDL emblemstats backgrounds 216 purchased 1", "statWriteDDL emblemstats backgrounds 216 unlocked 1", "statWriteDDL emblemstats backgrounds 217 purchased 1", "statWriteDDL emblemstats backgrounds 217 unlocked 1", "statWriteDDL emblemstats backgrounds 218 purchased 1", "statWriteDDL emblemstats backgrounds 218 unlocked 1", "statWriteDDL emblemstats backgrounds 219 purchased 1", "statWriteDDL emblemstats backgrounds 219 unlocked 1",
		"statWriteDDL emblemstats backgrounds 220 purchased 1", "statWriteDDL emblemstats backgrounds 220 unlocked 1", "statWriteDDL emblemstats backgrounds 221 purchased 1", "statWriteDDL emblemstats backgrounds 221 unlocked 1", "statWriteDDL emblemstats backgrounds 222 purchased 1", "statWriteDDL emblemstats backgrounds 222 unlocked 1", "statWriteDDL emblemstats backgrounds 223 purchased 1", "statWriteDDL emblemstats backgrounds 223 unlocked 1", "statWriteDDL emblemstats backgrounds 224 purchased 1", "statWriteDDL emblemstats backgrounds 224 unlocked 1",
		"statWriteDDL emblemstats backgrounds 225 purchased 1", "statWriteDDL emblemstats backgrounds 225 unlocked 1", "statWriteDDL emblemstats backgrounds 226 purchased 1", "statWriteDDL emblemstats backgrounds 226 unlocked 1", "statWriteDDL emblemstats backgrounds 227 purchased 1", "statWriteDDL emblemstats backgrounds 227 unlocked 1", "statWriteDDL emblemstats backgrounds 228 purchased 1", "statWriteDDL emblemstats backgrounds 228 unlocked 1", "statWriteDDL emblemstats backgrounds 229 purchased 1", "statWriteDDL emblemstats backgrounds 229 unlocked 1",
		"statWriteDDL emblemstats backgrounds 230 purchased 1", "statWriteDDL emblemstats backgrounds 230 unlocked 1", "statWriteDDL emblemstats backgrounds 231 purchased 1", "statWriteDDL emblemstats backgrounds 231 unlocked 1", "statWriteDDL emblemstats backgrounds 232 purchased 1", "statWriteDDL emblemstats backgrounds 232 unlocked 1", "statWriteDDL emblemstats backgrounds 233 purchased 1", "statWriteDDL emblemstats backgrounds 233 unlocked 1", "statWriteDDL emblemstats backgrounds 234 purchased 1", "statWriteDDL emblemstats backgrounds 234 unlocked 1",
		"statWriteDDL emblemstats backgrounds 235 purchased 1", "statWriteDDL emblemstats backgrounds 235 unlocked 1", "statWriteDDL emblemstats backgrounds 236 purchased 1", "statWriteDDL emblemstats backgrounds 236 unlocked 1", "statWriteDDL emblemstats backgrounds 237 purchased 1", "statWriteDDL emblemstats backgrounds 237 unlocked 1", "statWriteDDL emblemstats backgrounds 238 purchased 1", "statWriteDDL emblemstats backgrounds 238 unlocked 1", "statWriteDDL emblemstats backgrounds 239 purchased 1", "statWriteDDL emblemstats backgrounds 239 unlocked 1",
		"statWriteDDL emblemstats backgrounds 240 purchased 1", "statWriteDDL emblemstats backgrounds 240 unlocked 1", "statWriteDDL emblemstats backgrounds 241 purchased 1", "statWriteDDL emblemstats backgrounds 241 unlocked 1", "statWriteDDL emblemstats backgrounds 242 purchased 1", "statWriteDDL emblemstats backgrounds 242 unlocked 1", "statWriteDDL emblemstats backgrounds 243 purchased 1", "statWriteDDL emblemstats backgrounds 243 unlocked 1", "statWriteDDL emblemstats backgrounds 244 purchased 1", "statWriteDDL emblemstats backgrounds 244 unlocked 1",
		"statWriteDDL emblemstats backgrounds 245 purchased 1", "statWriteDDL emblemstats backgrounds 245 unlocked 1", "statWriteDDL emblemstats backgrounds 246 purchased 1", "statWriteDDL emblemstats backgrounds 246 unlocked 1", "statWriteDDL emblemstats backgrounds 247 purchased 1", "statWriteDDL emblemstats backgrounds 247 unlocked 1", "statWriteDDL emblemstats backgrounds 248 purchased 1", "statWriteDDL emblemstats backgrounds 248 unlocked 1", "statWriteDDL emblemstats backgrounds 249 purchased 1", "statWriteDDL emblemstats backgrounds 249 unlocked 1",
		"statWriteDDL emblemstats backgrounds 250 purchased 1", "statWriteDDL emblemstats backgrounds 250 unlocked 1", "statWriteDDL emblemstats backgrounds 251 purchased 1", "statWriteDDL emblemstats backgrounds 251 unlocked 1", "statWriteDDL emblemstats backgrounds 252 purchased 1", "statWriteDDL emblemstats backgrounds 252 unlocked 1", "statWriteDDL emblemstats backgrounds 253 purchased 1", "statWriteDDL emblemstats backgrounds 253 unlocked 1", "statWriteDDL emblemstats backgrounds 254 purchased 1", "statWriteDDL emblemstats backgrounds 254 unlocked 1",
		"statWriteDDL emblemstats backgrounds 255 purchased 1", "statWriteDDL emblemstats backgrounds 255 unlocked 1", "statWriteDDL emblemstats backgrounds 256 purchased 1", "statWriteDDL emblemstats backgrounds 256 unlocked 1", "statWriteDDL emblemstats backgrounds 257 purchased 1", "statWriteDDL emblemstats backgrounds 257 unlocked 1", "statWriteDDL emblemstats backgrounds 258 purchased 1", "statWriteDDL emblemstats backgrounds 258 unlocked 1", "statWriteDDL emblemstats backgrounds 259 purchased 1", "statWriteDDL emblemstats backgrounds 259 unlocked 1",
		"statWriteDDL emblemstats backgrounds 260 purchased 1", "statWriteDDL emblemstats backgrounds 260 unlocked 1", "statWriteDDL emblemstats backgrounds 261 purchased 1", "statWriteDDL emblemstats backgrounds 261 unlocked 1", "statWriteDDL emblemstats backgrounds 262 purchased 1", "statWriteDDL emblemstats backgrounds 262 unlocked 1", "statWriteDDL emblemstats backgrounds 263 purchased 1", "statWriteDDL emblemstats backgrounds 263 unlocked 1", "statWriteDDL emblemstats backgrounds 264 purchased 1", "statWriteDDL emblemstats backgrounds 264 unlocked 1",
		"statWriteDDL emblemstats backgrounds 265 purchased 1", "statWriteDDL emblemstats backgrounds 265 unlocked 1", "statWriteDDL emblemstats backgrounds 266 purchased 1", "statWriteDDL emblemstats backgrounds 266 unlocked 1", "statWriteDDL emblemstats backgrounds 267 purchased 1", "statWriteDDL emblemstats backgrounds 267 unlocked 1", "statWriteDDL emblemstats backgrounds 268 purchased 1", "statWriteDDL emblemstats backgrounds 268 unlocked 1", "statWriteDDL emblemstats backgrounds 269 purchased 1", "statWriteDDL emblemstats backgrounds 269 unlocked 1",
		"statWriteDDL emblemstats backgrounds 270 purchased 1", "statWriteDDL emblemstats backgrounds 270 unlocked 1", "statWriteDDL emblemstats backgrounds 271 purchased 1", "statWriteDDL emblemstats backgrounds 271 unlocked 1", "statWriteDDL emblemstats backgrounds 272 purchased 1", "statWriteDDL emblemstats backgrounds 272 unlocked 1", "statWriteDDL emblemstats backgrounds 273 purchased 1", "statWriteDDL emblemstats backgrounds 273 unlocked 1", "statWriteDDL emblemstats backgrounds 274 purchased 1", "statWriteDDL emblemstats backgrounds 274 unlocked 1",
		"statWriteDDL emblemstats backgrounds 275 purchased 1", "statWriteDDL emblemstats backgrounds 275 unlocked 1", "statWriteDDL emblemstats backgrounds 276 purchased 1", "statWriteDDL emblemstats backgrounds 276 unlocked 1", "statWriteDDL emblemstats backgrounds 277 purchased 1", "statWriteDDL emblemstats backgrounds 277 unlocked 1", "statWriteDDL emblemstats backgrounds 278 purchased 1", "statWriteDDL emblemstats backgrounds 278 unlocked 1", "statWriteDDL emblemstats backgrounds 279 purchased 1", "statWriteDDL emblemstats backgrounds 279 unlocked 1",
		"statWriteDDL emblemstats backgrounds 280 purchased 1", "statWriteDDL emblemstats backgrounds 280 unlocked 1", "statWriteDDL emblemstats backgrounds 281 purchased 1", "statWriteDDL emblemstats backgrounds 281 unlocked 1", "statWriteDDL emblemstats backgrounds 282 purchased 1", "statWriteDDL emblemstats backgrounds 282 unlocked 1", "statWriteDDL emblemstats backgrounds 283 purchased 1", "statWriteDDL emblemstats backgrounds 283 unlocked 1", "statWriteDDL emblemstats backgrounds 284 purchased 1", "statWriteDDL emblemstats backgrounds 284 unlocked 1",
		"statWriteDDL emblemstats backgrounds 285 purchased 1", "statWriteDDL emblemstats backgrounds 285 unlocked 1", "statWriteDDL emblemstats backgrounds 286 purchased 1", "statWriteDDL emblemstats backgrounds 286 unlocked 1", "statWriteDDL emblemstats backgrounds 287 purchased 1", "statWriteDDL emblemstats backgrounds 287 unlocked 1", "statWriteDDL emblemstats backgrounds 288 purchased 1", "statWriteDDL emblemstats backgrounds 288 unlocked 1", "statWriteDDL emblemstats backgrounds 289 purchased 1", "statWriteDDL emblemstats backgrounds 289 unlocked 1",
		"statWriteDDL emblemstats backgrounds 290 purchased 1", "statWriteDDL emblemstats backgrounds 290 unlocked 1", "statWriteDDL emblemstats backgrounds 291 purchased 1", "statWriteDDL emblemstats backgrounds 291 unlocked 1", "statWriteDDL emblemstats backgrounds 292 purchased 1", "statWriteDDL emblemstats backgrounds 292 unlocked 1", "statWriteDDL emblemstats backgrounds 293 purchased 1", "statWriteDDL emblemstats backgrounds 293 unlocked 1", "statWriteDDL emblemstats backgrounds 294 purchased 1", "statWriteDDL emblemstats backgrounds 294 unlocked 1",
		"statWriteDDL emblemstats backgrounds 295 purchased 1", "statWriteDDL emblemstats backgrounds 295 unlocked 1", "statWriteDDL emblemstats backgrounds 296 purchased 1", "statWriteDDL emblemstats backgrounds 296 unlocked 1", "statWriteDDL emblemstats backgrounds 297 purchased 1", "statWriteDDL emblemstats backgrounds 297 unlocked 1", "statWriteDDL emblemstats backgrounds 298 purchased 1", "statWriteDDL emblemstats backgrounds 298 unlocked 1", "statWriteDDL emblemstats backgrounds 299 purchased 1", "statWriteDDL emblemstats backgrounds 299 unlocked 1",
		"statWriteDDL emblemstats backgrounds 300 purchased 1", "statWriteDDL emblemstats backgrounds 300 unlocked 1", "statWriteDDL emblemstats backgrounds 301 purchased 1", "statWriteDDL emblemstats backgrounds 301 unlocked 1", "statWriteDDL emblemstats backgrounds 302 purchased 1", "statWriteDDL emblemstats backgrounds 302 unlocked 1", "statWriteDDL emblemstats backgrounds 303 purchased 1", "statWriteDDL emblemstats backgrounds 303 unlocked 1", "statWriteDDL emblemstats backgrounds 304 purchased 1", "statWriteDDL emblemstats backgrounds 304 unlocked 1",
		"statWriteDDL emblemstats backgrounds 305 purchased 1", "statWriteDDL emblemstats backgrounds 305 unlocked 1", "statWriteDDL emblemstats backgrounds 306 purchased 1", "statWriteDDL emblemstats backgrounds 306 unlocked 1", "statWriteDDL emblemstats backgrounds 307 purchased 1", "statWriteDDL emblemstats backgrounds 307 unlocked 1", "statWriteDDL emblemstats backgrounds 308 purchased 1", "statWriteDDL emblemstats backgrounds 308 unlocked 1", "statWriteDDL emblemstats backgrounds 309 purchased 1", "statWriteDDL emblemstats backgrounds 309 unlocked 1",
		"statWriteDDL emblemstats backgrounds 310 purchased 1", "statWriteDDL emblemstats backgrounds 310 unlocked 1", "statWriteDDL emblemstats backgrounds 311 purchased 1", "statWriteDDL emblemstats backgrounds 311 unlocked 1", "statWriteDDL emblemstats backgrounds 312 purchased 1", "statWriteDDL emblemstats backgrounds 312 unlocked 1", "statWriteDDL emblemstats backgrounds 313 purchased 1", "statWriteDDL emblemstats backgrounds 313 unlocked 1", "statWriteDDL emblemstats backgrounds 314 purchased 1", "statWriteDDL emblemstats backgrounds 314 unlocked 1",
		"statWriteDDL emblemstats backgrounds 315 purchased 1", "statWriteDDL emblemstats backgrounds 315 unlocked 1", "statWriteDDL emblemstats backgrounds 316 purchased 1", "statWriteDDL emblemstats backgrounds 316 unlocked 1", "statWriteDDL emblemstats backgrounds 317 purchased 1", "statWriteDDL emblemstats backgrounds 317 unlocked 1", "statWriteDDL emblemstats backgrounds 318 purchased 1", "statWriteDDL emblemstats backgrounds 318 unlocked 1", "statWriteDDL emblemstats backgrounds 319 purchased 1", "statWriteDDL emblemstats backgrounds 319 unlocked 1",
		"statWriteDDL emblemstats backgrounds 320 purchased 1", "statWriteDDL emblemstats backgrounds 320 unlocked 1", "statWriteDDL emblemstats backgrounds 321 purchased 1", "statWriteDDL emblemstats backgrounds 321 unlocked 1", "statWriteDDL emblemstats backgrounds 322 purchased 1", "statWriteDDL emblemstats backgrounds 322 unlocked 1", "statWriteDDL emblemstats backgrounds 323 purchased 1", "statWriteDDL emblemstats backgrounds 323 unlocked 1", "statWriteDDL emblemstats backgrounds 324 purchased 1", "statWriteDDL emblemstats backgrounds 324 unlocked 1",
		"statWriteDDL emblemstats backgrounds 325 purchased 1", "statWriteDDL emblemstats backgrounds 325 unlocked 1", "statWriteDDL emblemstats backgrounds 326 purchased 1", "statWriteDDL emblemstats backgrounds 326 unlocked 1", "statWriteDDL emblemstats backgrounds 327 purchased 1", "statWriteDDL emblemstats backgrounds 327 unlocked 1", "statWriteDDL emblemstats backgrounds 328 purchased 1", "statWriteDDL emblemstats backgrounds 328 unlocked 1", "statWriteDDL emblemstats backgrounds 329 purchased 1", "statWriteDDL emblemstats backgrounds 329 unlocked 1",
		"statWriteDDL emblemstats backgrounds 330 purchased 1", "statWriteDDL emblemstats backgrounds 330 unlocked 1", "statWriteDDL emblemstats backgrounds 331 purchased 1", "statWriteDDL emblemstats backgrounds 331 unlocked 1", "statWriteDDL emblemstats backgrounds 332 purchased 1", "statWriteDDL emblemstats backgrounds 332 unlocked 1", "statWriteDDL emblemstats backgrounds 333 purchased 1", "statWriteDDL emblemstats backgrounds 333 unlocked 1", "statWriteDDL emblemstats backgrounds 334 purchased 1", "statWriteDDL emblemstats backgrounds 334 unlocked 1",
		"statWriteDDL emblemstats backgrounds 335 purchased 1", "statWriteDDL emblemstats backgrounds 335 unlocked 1", "statWriteDDL emblemstats backgrounds 336 purchased 1", "statWriteDDL emblemstats backgrounds 336 unlocked 1", "statWriteDDL emblemstats backgrounds 337 purchased 1", "statWriteDDL emblemstats backgrounds 337 unlocked 1", "statWriteDDL emblemstats backgrounds 338 purchased 1", "statWriteDDL emblemstats backgrounds 338 unlocked 1", "statWriteDDL emblemstats backgrounds 339 purchased 1", "statWriteDDL emblemstats backgrounds 339 unlocked 1",
		"statWriteDDL emblemstats backgrounds 340 purchased 1", "statWriteDDL emblemstats backgrounds 340 unlocked 1", "statWriteDDL emblemstats backgrounds 341 purchased 1", "statWriteDDL emblemstats backgrounds 341 unlocked 1", "statWriteDDL emblemstats backgrounds 342 purchased 1", "statWriteDDL emblemstats backgrounds 342 unlocked 1", "statWriteDDL emblemstats backgrounds 343 purchased 1", "statWriteDDL emblemstats backgrounds 343 unlocked 1", "statWriteDDL emblemstats backgrounds 344 purchased 1", "statWriteDDL emblemstats backgrounds 344 unlocked 1",
		"statWriteDDL emblemstats backgrounds 345 purchased 1", "statWriteDDL emblemstats backgrounds 345 unlocked 1", "statWriteDDL emblemstats backgrounds 346 purchased 1", "statWriteDDL emblemstats backgrounds 346 unlocked 1", "statWriteDDL emblemstats backgrounds 347 purchased 1", "statWriteDDL emblemstats backgrounds 347 unlocked 1", "statWriteDDL emblemstats backgrounds 348 purchased 1", "statWriteDDL emblemstats backgrounds 348 unlocked 1", "statWriteDDL emblemstats backgrounds 349 purchased 1", "statWriteDDL emblemstats backgrounds 349 unlocked 1",
		"statWriteDDL emblemstats backgrounds 350 purchased 1", "statWriteDDL emblemstats backgrounds 350 unlocked 1", "statWriteDDL emblemstats backgrounds 351 purchased 1", "statWriteDDL emblemstats backgrounds 351 unlocked 1", "statWriteDDL emblemstats backgrounds 352 purchased 1", "statWriteDDL emblemstats backgrounds 352 unlocked 1", "statWriteDDL emblemstats backgrounds 353 purchased 1", "statWriteDDL emblemstats backgrounds 353 unlocked 1", "statWriteDDL emblemstats backgrounds 354 purchased 1", "statWriteDDL emblemstats backgrounds 354 unlocked 1",
		"statWriteDDL emblemstats backgrounds 355 purchased 1", "statWriteDDL emblemstats backgrounds 355 unlocked 1", "statWriteDDL emblemstats backgrounds 356 purchased 1", "statWriteDDL emblemstats backgrounds 356 unlocked 1", "statWriteDDL emblemstats backgrounds 357 purchased 1", "statWriteDDL emblemstats backgrounds 357 unlocked 1", "statWriteDDL emblemstats backgrounds 358 purchased 1", "statWriteDDL emblemstats backgrounds 358 unlocked 1", "statWriteDDL emblemstats backgrounds 359 purchased 1", "statWriteDDL emblemstats backgrounds 359 unlocked 1",
		"statWriteDDL emblemstats backgrounds 360 purchased 1", "statWriteDDL emblemstats backgrounds 360 unlocked 1", "statWriteDDL emblemstats backgrounds 361 purchased 1", "statWriteDDL emblemstats backgrounds 361 unlocked 1", "statWriteDDL emblemstats backgrounds 362 purchased 1", "statWriteDDL emblemstats backgrounds 362 unlocked 1", "statWriteDDL emblemstats backgrounds 363 purchased 1", "statWriteDDL emblemstats backgrounds 363 unlocked 1", "statWriteDDL emblemstats backgrounds 364 purchased 1", "statWriteDDL emblemstats backgrounds 364 unlocked 1",
		"statWriteDDL emblemstats backgrounds 365 purchased 1", "statWriteDDL emblemstats backgrounds 365 unlocked 1", "statWriteDDL emblemstats backgrounds 366 purchased 1", "statWriteDDL emblemstats backgrounds 366 unlocked 1", "statWriteDDL emblemstats backgrounds 367 purchased 1", "statWriteDDL emblemstats backgrounds 367 unlocked 1", "statWriteDDL emblemstats backgrounds 368 purchased 1", "statWriteDDL emblemstats backgrounds 368 unlocked 1", "statWriteDDL emblemstats backgrounds 369 purchased 1", "statWriteDDL emblemstats backgrounds 369 unlocked 1",
		"statWriteDDL emblemstats backgrounds 370 purchased 1", "statWriteDDL emblemstats backgrounds 370 unlocked 1", "statWriteDDL emblemstats backgrounds 371 purchased 1", "statWriteDDL emblemstats backgrounds 371 unlocked 1", "statWriteDDL emblemstats backgrounds 372 purchased 1", "statWriteDDL emblemstats backgrounds 372 unlocked 1", "statWriteDDL emblemstats backgrounds 373 purchased 1", "statWriteDDL emblemstats backgrounds 373 unlocked 1", "statWriteDDL emblemstats backgrounds 374 purchased 1", "statWriteDDL emblemstats backgrounds 374 unlocked 1",
		"statWriteDDL emblemstats backgrounds 375 purchased 1", "statWriteDDL emblemstats backgrounds 375 unlocked 1", "statWriteDDL emblemstats backgrounds 376 purchased 1", "statWriteDDL emblemstats backgrounds 376 unlocked 1", "statWriteDDL emblemstats backgrounds 377 purchased 1", "statWriteDDL emblemstats backgrounds 377 unlocked 1", "statWriteDDL emblemstats backgrounds 378 purchased 1", "statWriteDDL emblemstats backgrounds 378 unlocked 1", "statWriteDDL emblemstats backgrounds 379 purchased 1", "statWriteDDL emblemstats backgrounds 379 unlocked 1",
		"statWriteDDL emblemstats backgrounds 380 purchased 1", "statWriteDDL emblemstats backgrounds 380 unlocked 1", "statWriteDDL emblemstats backgrounds 381 purchased 1", "statWriteDDL emblemstats backgrounds 381 unlocked 1", "statWriteDDL emblemstats backgrounds 382 purchased 1", "statWriteDDL emblemstats backgrounds 382 unlocked 1", "statWriteDDL emblemstats backgrounds 383 purchased 1", "statWriteDDL emblemstats backgrounds 383 unlocked 1", "statWriteDDL emblemstats backgrounds 384 purchased 1", "statWriteDDL emblemstats backgrounds 384 unlocked 1",
		"statWriteDDL emblemstats backgrounds 385 purchased 1", "statWriteDDL emblemstats backgrounds 385 unlocked 1", "statWriteDDL emblemstats backgrounds 386 purchased 1", "statWriteDDL emblemstats backgrounds 386 unlocked 1", "statWriteDDL emblemstats backgrounds 387 purchased 1", "statWriteDDL emblemstats backgrounds 387 unlocked 1", "statWriteDDL emblemstats backgrounds 388 purchased 1", "statWriteDDL emblemstats backgrounds 388 unlocked 1", "statWriteDDL emblemstats backgrounds 389 purchased 1", "statWriteDDL emblemstats backgrounds 389 unlocked 1",
		"statWriteDDL emblemstats backgrounds 390 purchased 1", "statWriteDDL emblemstats backgrounds 390 unlocked 1", "statWriteDDL emblemstats backgrounds 391 purchased 1", "statWriteDDL emblemstats backgrounds 391 unlocked 1", "statWriteDDL emblemstats backgrounds 392 purchased 1", "statWriteDDL emblemstats backgrounds 392 unlocked 1", "statWriteDDL emblemstats backgrounds 393 purchased 1", "statWriteDDL emblemstats backgrounds 393 unlocked 1", "statWriteDDL emblemstats backgrounds 394 purchased 1", "statWriteDDL emblemstats backgrounds 394 unlocked 1",
		"statWriteDDL emblemstats backgrounds 395 purchased 1", "statWriteDDL emblemstats backgrounds 395 unlocked 1", "statWriteDDL emblemstats backgrounds 396 purchased 1", "statWriteDDL emblemstats backgrounds 396 unlocked 1", "statWriteDDL emblemstats backgrounds 397 purchased 1", "statWriteDDL emblemstats backgrounds 397 unlocked 1", "statWriteDDL emblemstats backgrounds 398 purchased 1", "statWriteDDL emblemstats backgrounds 398 unlocked 1", "statWriteDDL emblemstats backgrounds 399 purchased 1", "statWriteDDL emblemstats backgrounds 399 unlocked 1",
		"statWriteDDL emblemstats backgrounds 400 purchased 1", "statWriteDDL emblemstats backgrounds 400 unlocked 1", "statWriteDDL emblemstats backgrounds 401 purchased 1", "statWriteDDL emblemstats backgrounds 401 unlocked 1", "statWriteDDL emblemstats backgrounds 402 purchased 1", "statWriteDDL emblemstats backgrounds 402 unlocked 1", "statWriteDDL emblemstats backgrounds 403 purchased 1", "statWriteDDL emblemstats backgrounds 403 unlocked 1", "statWriteDDL emblemstats backgrounds 404 purchased 1", "statWriteDDL emblemstats backgrounds 404 unlocked 1",
		"statWriteDDL emblemstats backgrounds 405 purchased 1", "statWriteDDL emblemstats backgrounds 405 unlocked 1", "statWriteDDL emblemstats backgrounds 406 purchased 1", "statWriteDDL emblemstats backgrounds 406 unlocked 1", "statWriteDDL emblemstats backgrounds 407 purchased 1", "statWriteDDL emblemstats backgrounds 407 unlocked 1", "statWriteDDL emblemstats backgrounds 408 purchased 1", "statWriteDDL emblemstats backgrounds 408 unlocked 1", "statWriteDDL emblemstats backgrounds 409 purchased 1", "statWriteDDL emblemstats backgrounds 409 unlocked 1",
		"statWriteDDL emblemstats backgrounds 410 purchased 1", "statWriteDDL emblemstats backgrounds 410 unlocked 1", "statWriteDDL emblemstats backgrounds 411 purchased 1", "statWriteDDL emblemstats backgrounds 411 unlocked 1", "statWriteDDL emblemstats backgrounds 412 purchased 1", "statWriteDDL emblemstats backgrounds 412 unlocked 1", "statWriteDDL emblemstats backgrounds 413 purchased 1", "statWriteDDL emblemstats backgrounds 413 unlocked 1", "statWriteDDL emblemstats backgrounds 414 purchased 1", "statWriteDDL emblemstats backgrounds 414 unlocked 1",
		"statWriteDDL emblemstats backgrounds 415 purchased 1", "statWriteDDL emblemstats backgrounds 415 unlocked 1", "statWriteDDL emblemstats backgrounds 416 purchased 1", "statWriteDDL emblemstats backgrounds 416 unlocked 1", "statWriteDDL emblemstats backgrounds 417 purchased 1", "statWriteDDL emblemstats backgrounds 417 unlocked 1", "statWriteDDL emblemstats backgrounds 418 purchased 1", "statWriteDDL emblemstats backgrounds 418 unlocked 1", "statWriteDDL emblemstats backgrounds 419 purchased 1", "statWriteDDL emblemstats backgrounds 419 unlocked 1",
		"statWriteDDL emblemstats backgrounds 420 purchased 1", "statWriteDDL emblemstats backgrounds 420 unlocked 1", "statWriteDDL emblemstats backgrounds 421 purchased 1", "statWriteDDL emblemstats backgrounds 421 unlocked 1", "statWriteDDL emblemstats backgrounds 422 purchased 1", "statWriteDDL emblemstats backgrounds 422 unlocked 1", "statWriteDDL emblemstats backgrounds 423 purchased 1", "statWriteDDL emblemstats backgrounds 423 unlocked 1", "statWriteDDL emblemstats backgrounds 424 purchased 1", "statWriteDDL emblemstats backgrounds 424 unlocked 1",
		"statWriteDDL emblemstats backgrounds 425 purchased 1", "statWriteDDL emblemstats backgrounds 425 unlocked 1", "statWriteDDL emblemstats backgrounds 426 purchased 1", "statWriteDDL emblemstats backgrounds 426 unlocked 1", "statWriteDDL emblemstats backgrounds 427 purchased 1", "statWriteDDL emblemstats backgrounds 427 unlocked 1", "statWriteDDL emblemstats backgrounds 428 purchased 1", "statWriteDDL emblemstats backgrounds 428 unlocked 1", "statWriteDDL emblemstats backgrounds 429 purchased 1", "statWriteDDL emblemstats backgrounds 429 unlocked 1",
		"statWriteDDL emblemstats backgrounds 430 purchased 1", "statWriteDDL emblemstats backgrounds 430 unlocked 1", "statWriteDDL emblemstats backgrounds 431 purchased 1", "statWriteDDL emblemstats backgrounds 431 unlocked 1", "statWriteDDL emblemstats backgrounds 432 purchased 1", "statWriteDDL emblemstats backgrounds 432 unlocked 1", "statWriteDDL emblemstats backgrounds 433 purchased 1", "statWriteDDL emblemstats backgrounds 433 unlocked 1", "statWriteDDL emblemstats backgrounds 434 purchased 1", "statWriteDDL emblemstats backgrounds 434 unlocked 1",
		"statWriteDDL emblemstats backgrounds 435 purchased 1", "statWriteDDL emblemstats backgrounds 435 unlocked 1", "statWriteDDL emblemstats backgrounds 436 purchased 1", "statWriteDDL emblemstats backgrounds 436 unlocked 1", "statWriteDDL emblemstats backgrounds 437 purchased 1", "statWriteDDL emblemstats backgrounds 437 unlocked 1", "statWriteDDL emblemstats backgrounds 438 purchased 1", "statWriteDDL emblemstats backgrounds 438 unlocked 1", "statWriteDDL emblemstats backgrounds 439 purchased 1", "statWriteDDL emblemstats backgrounds 439 unlocked 1",
		"statWriteDDL emblemstats backgrounds 440 purchased 1", "statWriteDDL emblemstats backgrounds 440 unlocked 1", "statWriteDDL emblemstats backgrounds 441 purchased 1", "statWriteDDL emblemstats backgrounds 441 unlocked 1", "statWriteDDL emblemstats backgrounds 442 purchased 1", "statWriteDDL emblemstats backgrounds 442 unlocked 1", "statWriteDDL emblemstats backgrounds 443 purchased 1", "statWriteDDL emblemstats backgrounds 443 unlocked 1", "statWriteDDL emblemstats backgrounds 444 purchased 1", "statWriteDDL emblemstats backgrounds 444 unlocked 1",
		"statWriteDDL emblemstats backgrounds 445 purchased 1", "statWriteDDL emblemstats backgrounds 445 unlocked 1", "statWriteDDL emblemstats backgrounds 446 purchased 1", "statWriteDDL emblemstats backgrounds 446 unlocked 1", "statWriteDDL emblemstats backgrounds 447 purchased 1", "statWriteDDL emblemstats backgrounds 447 unlocked 1", "statWriteDDL emblemstats backgrounds 448 purchased 1", "statWriteDDL emblemstats backgrounds 448 unlocked 1", "statWriteDDL emblemstats backgrounds 449 purchased 1", "statWriteDDL emblemstats backgrounds 449 unlocked 1",
		"statWriteDDL emblemstats backgrounds 450 purchased 1", "statWriteDDL emblemstats backgrounds 450 unlocked 1", "statWriteDDL emblemstats backgrounds 451 purchased 1", "statWriteDDL emblemstats backgrounds 451 unlocked 1", "statWriteDDL emblemstats backgrounds 452 purchased 1", "statWriteDDL emblemstats backgrounds 452 unlocked 1", "statWriteDDL emblemstats backgrounds 453 purchased 1", "statWriteDDL emblemstats backgrounds 453 unlocked 1", "statWriteDDL emblemstats backgrounds 454 purchased 1", "statWriteDDL emblemstats backgrounds 454 unlocked 1",
		"statWriteDDL emblemstats backgrounds 455 purchased 1", "statWriteDDL emblemstats backgrounds 455 unlocked 1", "statWriteDDL emblemstats backgrounds 456 purchased 1", "statWriteDDL emblemstats backgrounds 456 unlocked 1", "statWriteDDL emblemstats backgrounds 457 purchased 1", "statWriteDDL emblemstats backgrounds 457 unlocked 1", "statWriteDDL emblemstats backgrounds 458 purchased 1", "statWriteDDL emblemstats backgrounds 458 unlocked 1", "statWriteDDL emblemstats backgrounds 459 purchased 1", "statWriteDDL emblemstats backgrounds 459 unlocked 1",
		"statWriteDDL emblemstats backgrounds 460 purchased 1", "statWriteDDL emblemstats backgrounds 460 unlocked 1", "statWriteDDL emblemstats backgrounds 461 purchased 1", "statWriteDDL emblemstats backgrounds 461 unlocked 1", "statWriteDDL emblemstats backgrounds 462 purchased 1", "statWriteDDL emblemstats backgrounds 462 unlocked 1", "statWriteDDL emblemstats backgrounds 463 purchased 1", "statWriteDDL emblemstats backgrounds 463 unlocked 1", "statWriteDDL emblemstats backgrounds 464 purchased 1", "statWriteDDL emblemstats backgrounds 464 unlocked 1",
		"statWriteDDL emblemstats backgrounds 465 purchased 1", "statWriteDDL emblemstats backgrounds 465 unlocked 1", "statWriteDDL emblemstats backgrounds 466 purchased 1", "statWriteDDL emblemstats backgrounds 466 unlocked 1", "statWriteDDL emblemstats backgrounds 467 purchased 1", "statWriteDDL emblemstats backgrounds 467 unlocked 1", "statWriteDDL emblemstats backgrounds 468 purchased 1", "statWriteDDL emblemstats backgrounds 468 unlocked 1", "statWriteDDL emblemstats backgrounds 469 purchased 1", "statWriteDDL emblemstats backgrounds 469 unlocked 1",
		"statWriteDDL emblemstats backgrounds 470 purchased 1", "statWriteDDL emblemstats backgrounds 470 unlocked 1", "statWriteDDL emblemstats backgrounds 471 purchased 1", "statWriteDDL emblemstats backgrounds 471 unlocked 1", "statWriteDDL emblemstats backgrounds 472 purchased 1", "statWriteDDL emblemstats backgrounds 472 unlocked 1", "statWriteDDL emblemstats backgrounds 473 purchased 1", "statWriteDDL emblemstats backgrounds 473 unlocked 1", "statWriteDDL emblemstats backgrounds 474 purchased 1", "statWriteDDL emblemstats backgrounds 474 unlocked 1",
		"statWriteDDL emblemstats backgrounds 475 purchased 1", "statWriteDDL emblemstats backgrounds 475 unlocked 1", "statWriteDDL emblemstats backgrounds 476 purchased 1", "statWriteDDL emblemstats backgrounds 476 unlocked 1", "statWriteDDL emblemstats backgrounds 477 purchased 1", "statWriteDDL emblemstats backgrounds 477 unlocked 1", "statWriteDDL emblemstats backgrounds 478 purchased 1", "statWriteDDL emblemstats backgrounds 478 unlocked 1", "statWriteDDL emblemstats backgrounds 479 purchased 1", "statWriteDDL emblemstats backgrounds 479 unlocked 1",
		"statWriteDDL emblemstats backgrounds 480 purchased 1", "statWriteDDL emblemstats backgrounds 480 unlocked 1", "statWriteDDL emblemstats backgrounds 481 purchased 1", "statWriteDDL emblemstats backgrounds 481 unlocked 1", "statWriteDDL emblemstats backgrounds 482 purchased 1", "statWriteDDL emblemstats backgrounds 482 unlocked 1", "statWriteDDL emblemstats backgrounds 483 purchased 1", "statWriteDDL emblemstats backgrounds 483 unlocked 1", "statWriteDDL emblemstats backgrounds 484 purchased 1", "statWriteDDL emblemstats backgrounds 484 unlocked 1",
		"statWriteDDL emblemstats backgrounds 485 purchased 1", "statWriteDDL emblemstats backgrounds 485 unlocked 1", "statWriteDDL emblemstats backgrounds 486 purchased 1", "statWriteDDL emblemstats backgrounds 486 unlocked 1", "statWriteDDL emblemstats backgrounds 487 purchased 1", "statWriteDDL emblemstats backgrounds 487 unlocked 1", "statWriteDDL emblemstats backgrounds 488 purchased 1", "statWriteDDL emblemstats backgrounds 488 unlocked 1", "statWriteDDL emblemstats backgrounds 489 purchased 1", "statWriteDDL emblemstats backgrounds 489 unlocked 1",
		"statWriteDDL emblemstats backgrounds 490 purchased 1", "statWriteDDL emblemstats backgrounds 490 unlocked 1", "statWriteDDL emblemstats backgrounds 491 purchased 1", "statWriteDDL emblemstats backgrounds 491 unlocked 1", "statWriteDDL emblemstats backgrounds 492 purchased 1", "statWriteDDL emblemstats backgrounds 492 unlocked 1", "statWriteDDL emblemstats backgrounds 493 purchased 1", "statWriteDDL emblemstats backgrounds 493 unlocked 1", "statWriteDDL emblemstats backgrounds 494 purchased 1", "statWriteDDL emblemstats backgrounds 494 unlocked 1",
		"statWriteDDL emblemstats backgrounds 495 purchased 1", "statWriteDDL emblemstats backgrounds 495 unlocked 1", "statWriteDDL emblemstats backgrounds 496 purchased 1", "statWriteDDL emblemstats backgrounds 496 unlocked 1", "statWriteDDL emblemstats backgrounds 497 purchased 1", "statWriteDDL emblemstats backgrounds 497 unlocked 1", "statWriteDDL emblemstats backgrounds 498 purchased 1", "statWriteDDL emblemstats backgrounds 498 unlocked 1", "statWriteDDL emblemstats backgrounds 499 purchased 1", "statWriteDDL emblemstats backgrounds 499 unlocked 1",
		"statWriteDDL emblemstats backgrounds 500 purchased 1", "statWriteDDL emblemstats backgrounds 500 unlocked 1", "statWriteDDL emblemstats backgrounds 501 purchased 1", "statWriteDDL emblemstats backgrounds 501 unlocked 1", "statWriteDDL emblemstats backgrounds 502 purchased 1", "statWriteDDL emblemstats backgrounds 502 unlocked 1", "statWriteDDL emblemstats backgrounds 503 purchased 1", "statWriteDDL emblemstats backgrounds 503 unlocked 1", "statWriteDDL emblemstats backgrounds 504 purchased 1", "statWriteDDL emblemstats backgrounds 504 unlocked 1",
		"statWriteDDL emblemstats backgrounds 505 purchased 1", "statWriteDDL emblemstats backgrounds 505 unlocked 1", "statWriteDDL emblemstats backgrounds 506 purchased 1", "statWriteDDL emblemstats backgrounds 506 unlocked 1", "statWriteDDL emblemstats backgrounds 507 purchased 1", "statWriteDDL emblemstats backgrounds 507 unlocked 1", "statWriteDDL emblemstats backgrounds 508 purchased 1", "statWriteDDL emblemstats backgrounds 508 unlocked 1", "statWriteDDL emblemstats backgrounds 509 purchased 1", "statWriteDDL emblemstats backgrounds 509 unlocked 1",
		"statWriteDDL emblemstats backgrounds 510 purchased 1", "statWriteDDL emblemstats backgrounds 510 unlocked 1", "statWriteDDL emblemstats backgrounds 511 purchased 1", "statWriteDDL emblemstats backgrounds 511 unlocked 1", "statWriteDDL emblemstats backgrounds 512 purchased 1", "statWriteDDL emblemstats backgrounds 512 unlocked 1", "statWriteDDL emblemstats backgrounds 513 purchased 1", "statWriteDDL emblemstats backgrounds 513 unlocked 1", "statWriteDDL emblemstats backgrounds 514 purchased 1", "statWriteDDL emblemstats backgrounds 514 unlocked 1",
		"statWriteDDL emblemstats backgrounds 515 purchased 1", "statWriteDDL emblemstats backgrounds 515 unlocked 1", "statWriteDDL emblemstats backgrounds 516 purchased 1", "statWriteDDL emblemstats backgrounds 516 unlocked 1", "statWriteDDL emblemstats backgrounds 517 purchased 1", "statWriteDDL emblemstats backgrounds 517 unlocked 1", "statWriteDDL emblemstats backgrounds 518 purchased 1", "statWriteDDL emblemstats backgrounds 518 unlocked 1", "statWriteDDL emblemstats backgrounds 519 purchased 1", "statWriteDDL emblemstats backgrounds 519 unlocked 1",
		"statWriteDDL emblemstats backgrounds 520 purchased 1", "statWriteDDL emblemstats backgrounds 520 unlocked 1", "statWriteDDL emblemstats backgrounds 521 purchased 1", "statWriteDDL emblemstats backgrounds 521 unlocked 1", "statWriteDDL emblemstats backgrounds 522 purchased 1", "statWriteDDL emblemstats backgrounds 522 unlocked 1", "statWriteDDL emblemstats backgrounds 523 purchased 1", "statWriteDDL emblemstats backgrounds 523 unlocked 1", "statWriteDDL emblemstats backgrounds 524 purchased 1", "statWriteDDL emblemstats backgrounds 524 unlocked 1",
		"statWriteDDL emblemstats backgrounds 525 purchased 1", "statWriteDDL emblemstats backgrounds 525 unlocked 1", "statWriteDDL emblemstats backgrounds 526 purchased 1", "statWriteDDL emblemstats backgrounds 526 unlocked 1", "statWriteDDL emblemstats backgrounds 527 purchased 1", "statWriteDDL emblemstats backgrounds 527 unlocked 1", "statWriteDDL emblemstats backgrounds 528 purchased 1", "statWriteDDL emblemstats backgrounds 528 unlocked 1", "statWriteDDL emblemstats backgrounds 529 purchased 1", "statWriteDDL emblemstats backgrounds 529 unlocked 1",
		"statWriteDDL emblemstats backgrounds 530 purchased 1", "statWriteDDL emblemstats backgrounds 530 unlocked 1", "statWriteDDL emblemstats backgrounds 531 purchased 1", "statWriteDDL emblemstats backgrounds 531 unlocked 1", "statWriteDDL emblemstats backgrounds 532 purchased 1", "statWriteDDL emblemstats backgrounds 532 unlocked 1", "statWriteDDL emblemstats backgrounds 533 purchased 1", "statWriteDDL emblemstats backgrounds 533 unlocked 1", "statWriteDDL emblemstats backgrounds 534 purchased 1", "statWriteDDL emblemstats backgrounds 534 unlocked 1",
		"statWriteDDL emblemstats backgrounds 535 purchased 1", "statWriteDDL emblemstats backgrounds 535 unlocked 1", "statWriteDDL emblemstats backgrounds 536 purchased 1", "statWriteDDL emblemstats backgrounds 536 unlocked 1", "statWriteDDL emblemstats backgrounds 537 purchased 1", "statWriteDDL emblemstats backgrounds 537 unlocked 1", "statWriteDDL emblemstats backgrounds 538 purchased 1", "statWriteDDL emblemstats backgrounds 538 unlocked 1", "statWriteDDL emblemstats backgrounds 539 purchased 1", "statWriteDDL emblemstats backgrounds 539 unlocked 1",
		"statWriteDDL emblemstats backgrounds 540 purchased 1", "statWriteDDL emblemstats backgrounds 540 unlocked 1", "statWriteDDL emblemstats backgrounds 541 purchased 1", "statWriteDDL emblemstats backgrounds 541 unlocked 1", "statWriteDDL emblemstats backgrounds 542 purchased 1", "statWriteDDL emblemstats backgrounds 542 unlocked 1", "statWriteDDL emblemstats backgrounds 543 purchased 1", "statWriteDDL emblemstats backgrounds 543 unlocked 1", "statWriteDDL emblemstats backgrounds 544 purchased 1", "statWriteDDL emblemstats backgrounds 544 unlocked 1",
		"statWriteDDL emblemstats backgrounds 545 purchased 1", "statWriteDDL emblemstats backgrounds 545 unlocked 1", "statWriteDDL emblemstats backgrounds 546 purchased 1", "statWriteDDL emblemstats backgrounds 546 unlocked 1", "statWriteDDL emblemstats backgrounds 547 purchased 1", "statWriteDDL emblemstats backgrounds 547 unlocked 1", "statWriteDDL emblemstats backgrounds 548 purchased 1", "statWriteDDL emblemstats backgrounds 548 unlocked 1", "statWriteDDL emblemstats backgrounds 549 purchased 1", "statWriteDDL emblemstats backgrounds 549 unlocked 1",
		"statWriteDDL emblemstats backgrounds 550 purchased 1", "statWriteDDL emblemstats backgrounds 550 unlocked 1", "statWriteDDL emblemstats backgrounds 551 purchased 1", "statWriteDDL emblemstats backgrounds 551 unlocked 1", "statWriteDDL emblemstats backgrounds 552 purchased 1", "statWriteDDL emblemstats backgrounds 552 unlocked 1", "statWriteDDL emblemstats backgrounds 553 purchased 1", "statWriteDDL emblemstats backgrounds 553 unlocked 1", "statWriteDDL emblemstats backgrounds 554 purchased 1", "statWriteDDL emblemstats backgrounds 554 unlocked 1",
		"statWriteDDL emblemstats backgrounds 555 purchased 1", "statWriteDDL emblemstats backgrounds 555 unlocked 1", "statWriteDDL emblemstats backgrounds 556 purchased 1", "statWriteDDL emblemstats backgrounds 556 unlocked 1", "statWriteDDL emblemstats backgrounds 557 purchased 1", "statWriteDDL emblemstats backgrounds 557 unlocked 1", "statWriteDDL emblemstats backgrounds 558 purchased 1", "statWriteDDL emblemstats backgrounds 558 unlocked 1", "statWriteDDL emblemstats backgrounds 559 purchased 1", "statWriteDDL emblemstats backgrounds 559 unlocked 1",
		"statWriteDDL emblemstats backgrounds 560 purchased 1", "statWriteDDL emblemstats backgrounds 560 unlocked 1", "statWriteDDL emblemstats backgrounds 561 purchased 1", "statWriteDDL emblemstats backgrounds 561 unlocked 1", "statWriteDDL emblemstats backgrounds 562 purchased 1", "statWriteDDL emblemstats backgrounds 562 unlocked 1", "statWriteDDL emblemstats backgrounds 563 purchased 1", "statWriteDDL emblemstats backgrounds 563 unlocked 1", "statWriteDDL emblemstats backgrounds 564 purchased 1", "statWriteDDL emblemstats backgrounds 564 unlocked 1",
		"statWriteDDL emblemstats backgrounds 565 purchased 1", "statWriteDDL emblemstats backgrounds 565 unlocked 1", "statWriteDDL emblemstats backgrounds 566 purchased 1", "statWriteDDL emblemstats backgrounds 566 unlocked 1", "statWriteDDL emblemstats backgrounds 567 purchased 1", "statWriteDDL emblemstats backgrounds 567 unlocked 1", "statWriteDDL emblemstats backgrounds 568 purchased 1", "statWriteDDL emblemstats backgrounds 568 unlocked 1", "statWriteDDL emblemstats backgrounds 569 purchased 1", "statWriteDDL emblemstats backgrounds 569 unlocked 1",
		"statWriteDDL emblemstats backgrounds 570 purchased 1", "statWriteDDL emblemstats backgrounds 570 unlocked 1", "statWriteDDL emblemstats backgrounds 571 purchased 1", "statWriteDDL emblemstats backgrounds 571 unlocked 1", "statWriteDDL emblemstats backgrounds 572 purchased 1", "statWriteDDL emblemstats backgrounds 572 unlocked 1", "statWriteDDL emblemstats backgrounds 573 purchased 1", "statWriteDDL emblemstats backgrounds 573 unlocked 1", "statWriteDDL emblemstats backgrounds 574 purchased 1", "statWriteDDL emblemstats backgrounds 574 unlocked 1",
		"statWriteDDL emblemstats backgrounds 575 purchased 1", "statWriteDDL emblemstats backgrounds 575 unlocked 1", "statWriteDDL emblemstats icons 0 purchased 1", "statWriteDDL emblemstats icons 0 unlocked 1", "statWriteDDL emblemstats icons 1 purchased 1", "statWriteDDL emblemstats icons 1 unlocked 1", "statWriteDDL emblemstats icons 2 purchased 1", "statWriteDDL emblemstats icons 2 unlocked 1", "statWriteDDL emblemstats icons 3 purchased 1", "statWriteDDL emblemstats icons 3 unlocked 1",
		"statWriteDDL emblemstats icons 4 purchased 1", "statWriteDDL emblemstats icons 4 unlocked 1", "statWriteDDL emblemstats icons 5 purchased 1", "statWriteDDL emblemstats icons 5 unlocked 1", "statWriteDDL emblemstats icons 6 purchased 1", "statWriteDDL emblemstats icons 6 unlocked 1", "statWriteDDL emblemstats icons 7 purchased 1", "statWriteDDL emblemstats icons 7 unlocked 1", "statWriteDDL emblemstats icons 8 purchased 1", "statWriteDDL emblemstats icons 8 unlocked 1",
		"statWriteDDL emblemstats icons 9 purchased 1", "statWriteDDL emblemstats icons 9 unlocked 1", "statWriteDDL emblemstats icons 10 purchased 1", "statWriteDDL emblemstats icons 10 unlocked 1", "statWriteDDL emblemstats icons 11 purchased 1", "statWriteDDL emblemstats icons 11 unlocked 1", "statWriteDDL emblemstats icons 12 purchased 1", "statWriteDDL emblemstats icons 12 unlocked 1", "statWriteDDL emblemstats icons 13 purchased 1", "statWriteDDL emblemstats icons 13 unlocked 1",
		"statWriteDDL emblemstats icons 14 purchased 1", "statWriteDDL emblemstats icons 14 unlocked 1", "statWriteDDL emblemstats icons 15 purchased 1", "statWriteDDL emblemstats icons 15 unlocked 1", "statWriteDDL emblemstats icons 16 purchased 1", "statWriteDDL emblemstats icons 16 unlocked 1", "statWriteDDL emblemstats icons 17 purchased 1", "statWriteDDL emblemstats icons 17 unlocked 1", "statWriteDDL emblemstats icons 18 purchased 1", "statWriteDDL emblemstats icons 18 unlocked 1",
		"statWriteDDL emblemstats icons 19 purchased 1", "statWriteDDL emblemstats icons 19 unlocked 1", "statWriteDDL emblemstats icons 20 purchased 1", "statWriteDDL emblemstats icons 20 unlocked 1", "statWriteDDL emblemstats icons 21 purchased 1", "statWriteDDL emblemstats icons 21 unlocked 1", "statWriteDDL emblemstats icons 22 purchased 1", "statWriteDDL emblemstats icons 22 unlocked 1", "statWriteDDL emblemstats icons 23 purchased 1", "statWriteDDL emblemstats icons 23 unlocked 1",
		"statWriteDDL emblemstats icons 24 purchased 1", "statWriteDDL emblemstats icons 24 unlocked 1", "statWriteDDL emblemstats icons 25 purchased 1", "statWriteDDL emblemstats icons 25 unlocked 1", "statWriteDDL emblemstats icons 26 purchased 1", "statWriteDDL emblemstats icons 26 unlocked 1", "statWriteDDL emblemstats icons 27 purchased 1", "statWriteDDL emblemstats icons 27 unlocked 1", "statWriteDDL emblemstats icons 28 purchased 1", "statWriteDDL emblemstats icons 28 unlocked 1",
		"statWriteDDL emblemstats icons 29 purchased 1", "statWriteDDL emblemstats icons 29 unlocked 1", "statWriteDDL emblemstats icons 30 purchased 1", "statWriteDDL emblemstats icons 30 unlocked 1", "statWriteDDL emblemstats icons 31 purchased 1", "statWriteDDL emblemstats icons 31 unlocked 1", "statWriteDDL emblemstats icons 32 purchased 1", "statWriteDDL emblemstats icons 32 unlocked 1", "statWriteDDL emblemstats icons 33 purchased 1", "statWriteDDL emblemstats icons 33 unlocked 1",
		"statWriteDDL emblemstats icons 34 purchased 1", "statWriteDDL emblemstats icons 34 unlocked 1", "statWriteDDL emblemstats icons 35 purchased 1", "statWriteDDL emblemstats icons 35 unlocked 1", "statWriteDDL emblemstats icons 36 purchased 1", "statWriteDDL emblemstats icons 36 unlocked 1", "statWriteDDL emblemstats icons 37 purchased 1", "statWriteDDL emblemstats icons 37 unlocked 1", "statWriteDDL emblemstats icons 38 purchased 1", "statWriteDDL emblemstats icons 38 unlocked 1",
		"statWriteDDL emblemstats icons 39 purchased 1", "statWriteDDL emblemstats icons 39 unlocked 1", "statWriteDDL emblemstats icons 40 purchased 1", "statWriteDDL emblemstats icons 40 unlocked 1", "statWriteDDL emblemstats icons 41 purchased 1", "statWriteDDL emblemstats icons 41 unlocked 1", "statWriteDDL emblemstats icons 42 purchased 1", "statWriteDDL emblemstats icons 42 unlocked 1", "statWriteDDL emblemstats icons 43 purchased 1", "statWriteDDL emblemstats icons 43 unlocked 1",
		"statWriteDDL emblemstats icons 44 purchased 1", "statWriteDDL emblemstats icons 44 unlocked 1", "statWriteDDL emblemstats icons 45 purchased 1", "statWriteDDL emblemstats icons 45 unlocked 1", "statWriteDDL emblemstats icons 46 purchased 1", "statWriteDDL emblemstats icons 46 unlocked 1", "statWriteDDL emblemstats icons 47 purchased 1", "statWriteDDL emblemstats icons 47 unlocked 1", "statWriteDDL emblemstats icons 48 purchased 1", "statWriteDDL emblemstats icons 48 unlocked 1",
		"statWriteDDL emblemstats icons 49 purchased 1", "statWriteDDL emblemstats icons 49 unlocked 1", "statWriteDDL emblemstats icons 50 purchased 1", "statWriteDDL emblemstats icons 50 unlocked 1", "statWriteDDL emblemstats icons 51 purchased 1", "statWriteDDL emblemstats icons 51 unlocked 1", "statWriteDDL emblemstats icons 52 purchased 1", "statWriteDDL emblemstats icons 52 unlocked 1", "statWriteDDL emblemstats icons 53 purchased 1", "statWriteDDL emblemstats icons 53 unlocked 1",
		"statWriteDDL emblemstats icons 54 purchased 1", "statWriteDDL emblemstats icons 54 unlocked 1", "statWriteDDL emblemstats icons 55 purchased 1", "statWriteDDL emblemstats icons 55 unlocked 1", "statWriteDDL emblemstats icons 56 purchased 1", "statWriteDDL emblemstats icons 56 unlocked 1", "statWriteDDL emblemstats icons 57 purchased 1", "statWriteDDL emblemstats icons 57 unlocked 1", "statWriteDDL emblemstats icons 58 purchased 1", "statWriteDDL emblemstats icons 58 unlocked 1",
		"statWriteDDL emblemstats icons 59 purchased 1", "statWriteDDL emblemstats icons 59 unlocked 1", "statWriteDDL emblemstats icons 60 purchased 1", "statWriteDDL emblemstats icons 60 unlocked 1", "statWriteDDL emblemstats icons 61 purchased 1", "statWriteDDL emblemstats icons 61 unlocked 1", "statWriteDDL emblemstats icons 62 purchased 1", "statWriteDDL emblemstats icons 62 unlocked 1", "statWriteDDL emblemstats icons 63 purchased 1", "statWriteDDL emblemstats icons 63 unlocked 1",
		"statWriteDDL emblemstats icons 64 purchased 1", "statWriteDDL emblemstats icons 64 unlocked 1", "statWriteDDL emblemstats icons 65 purchased 1", "statWriteDDL emblemstats icons 65 unlocked 1", "statWriteDDL emblemstats icons 66 purchased 1", "statWriteDDL emblemstats icons 66 unlocked 1", "statWriteDDL emblemstats icons 67 purchased 1", "statWriteDDL emblemstats icons 67 unlocked 1", "statWriteDDL emblemstats icons 68 purchased 1", "statWriteDDL emblemstats icons 68 unlocked 1",
		"statWriteDDL emblemstats icons 69 purchased 1", "statWriteDDL emblemstats icons 69 unlocked 1", "statWriteDDL emblemstats icons 70 purchased 1", "statWriteDDL emblemstats icons 70 unlocked 1", "statWriteDDL emblemstats icons 71 purchased 1", "statWriteDDL emblemstats icons 71 unlocked 1", "statWriteDDL emblemstats icons 72 purchased 1", "statWriteDDL emblemstats icons 72 unlocked 1", "statWriteDDL emblemstats icons 73 purchased 1", "statWriteDDL emblemstats icons 73 unlocked 1",
		"statWriteDDL emblemstats icons 74 purchased 1", "statWriteDDL emblemstats icons 74 unlocked 1", "statWriteDDL emblemstats icons 75 purchased 1", "statWriteDDL emblemstats icons 75 unlocked 1", "statWriteDDL emblemstats icons 76 purchased 1", "statWriteDDL emblemstats icons 76 unlocked 1", "statWriteDDL emblemstats icons 77 purchased 1", "statWriteDDL emblemstats icons 77 unlocked 1", "statWriteDDL emblemstats icons 78 purchased 1", "statWriteDDL emblemstats icons 78 unlocked 1",
		"statWriteDDL emblemstats icons 79 purchased 1", "statWriteDDL emblemstats icons 79 unlocked 1", "statWriteDDL emblemstats icons 80 purchased 1", "statWriteDDL emblemstats icons 80 unlocked 1", "statWriteDDL emblemstats icons 81 purchased 1", "statWriteDDL emblemstats icons 81 unlocked 1", "statWriteDDL emblemstats icons 82 purchased 1", "statWriteDDL emblemstats icons 82 unlocked 1", "statWriteDDL emblemstats icons 83 purchased 1", "statWriteDDL emblemstats icons 83 unlocked 1",
		"statWriteDDL emblemstats icons 84 purchased 1", "statWriteDDL emblemstats icons 84 unlocked 1", "statWriteDDL emblemstats icons 85 purchased 1", "statWriteDDL emblemstats icons 85 unlocked 1", "statWriteDDL emblemstats icons 86 purchased 1", "statWriteDDL emblemstats icons 86 unlocked 1", "statWriteDDL emblemstats icons 87 purchased 1", "statWriteDDL emblemstats icons 87 unlocked 1", "statWriteDDL emblemstats icons 88 purchased 1", "statWriteDDL emblemstats icons 88 unlocked 1",
		"statWriteDDL emblemstats icons 89 purchased 1", "statWriteDDL emblemstats icons 89 unlocked 1", "statWriteDDL emblemstats icons 90 purchased 1", "statWriteDDL emblemstats icons 90 unlocked 1", "statWriteDDL emblemstats icons 91 purchased 1", "statWriteDDL emblemstats icons 91 unlocked 1", "statWriteDDL emblemstats icons 92 purchased 1", "statWriteDDL emblemstats icons 92 unlocked 1", "statWriteDDL emblemstats icons 93 purchased 1", "statWriteDDL emblemstats icons 93 unlocked 1",
		"statWriteDDL emblemstats icons 94 purchased 1", "statWriteDDL emblemstats icons 94 unlocked 1", "statWriteDDL emblemstats icons 95 purchased 1", "statWriteDDL emblemstats icons 95 unlocked 1", "statWriteDDL emblemstats icons 96 purchased 1", "statWriteDDL emblemstats icons 96 unlocked 1", "statWriteDDL emblemstats icons 97 purchased 1", "statWriteDDL emblemstats icons 97 unlocked 1", "statWriteDDL emblemstats icons 98 purchased 1", "statWriteDDL emblemstats icons 98 unlocked 1",
		"statWriteDDL emblemstats icons 99 purchased 1", "statWriteDDL emblemstats icons 99 unlocked 1", "statWriteDDL emblemstats icons 100 purchased 1", "statWriteDDL emblemstats icons 100 unlocked 1", "statWriteDDL emblemstats icons 101 purchased 1", "statWriteDDL emblemstats icons 101 unlocked 1", "statWriteDDL emblemstats icons 102 purchased 1", "statWriteDDL emblemstats icons 102 unlocked 1", "statWriteDDL emblemstats icons 103 purchased 1", "statWriteDDL emblemstats icons 103 unlocked 1",
		"statWriteDDL emblemstats icons 104 purchased 1", "statWriteDDL emblemstats icons 104 unlocked 1", "statWriteDDL emblemstats icons 105 purchased 1", "statWriteDDL emblemstats icons 105 unlocked 1", "statWriteDDL emblemstats icons 106 purchased 1", "statWriteDDL emblemstats icons 106 unlocked 1", "statWriteDDL emblemstats icons 107 purchased 1", "statWriteDDL emblemstats icons 107 unlocked 1", "statWriteDDL emblemstats icons 108 purchased 1", "statWriteDDL emblemstats icons 108 unlocked 1",
		"statWriteDDL emblemstats icons 109 purchased 1", "statWriteDDL emblemstats icons 109 unlocked 1", "statWriteDDL emblemstats icons 110 purchased 1", "statWriteDDL emblemstats icons 110 unlocked 1", "statWriteDDL emblemstats icons 111 purchased 1", "statWriteDDL emblemstats icons 111 unlocked 1", "statWriteDDL emblemstats icons 112 purchased 1", "statWriteDDL emblemstats icons 112 unlocked 1", "statWriteDDL emblemstats icons 113 purchased 1", "statWriteDDL emblemstats icons 113 unlocked 1",
		"statWriteDDL emblemstats icons 114 purchased 1", "statWriteDDL emblemstats icons 114 unlocked 1", "statWriteDDL emblemstats icons 115 purchased 1", "statWriteDDL emblemstats icons 115 unlocked 1", "statWriteDDL emblemstats icons 116 purchased 1", "statWriteDDL emblemstats icons 116 unlocked 1", "statWriteDDL emblemstats icons 117 purchased 1", "statWriteDDL emblemstats icons 117 unlocked 1", "statWriteDDL emblemstats icons 118 purchased 1", "statWriteDDL emblemstats icons 118 unlocked 1",
		"statWriteDDL emblemstats icons 119 purchased 1", "statWriteDDL emblemstats icons 119 unlocked 1", "statWriteDDL emblemstats icons 120 purchased 1", "statWriteDDL emblemstats icons 120 unlocked 1", "statWriteDDL emblemstats icons 121 purchased 1", "statWriteDDL emblemstats icons 121 unlocked 1", "statWriteDDL emblemstats icons 122 purchased 1", "statWriteDDL emblemstats icons 122 unlocked 1", "statWriteDDL emblemstats icons 123 purchased 1", "statWriteDDL emblemstats icons 123 unlocked 1",
		"statWriteDDL emblemstats icons 124 purchased 1", "statWriteDDL emblemstats icons 124 unlocked 1", "statWriteDDL emblemstats icons 125 purchased 1", "statWriteDDL emblemstats icons 125 unlocked 1", "statWriteDDL emblemstats icons 126 purchased 1", "statWriteDDL emblemstats icons 126 unlocked 1", "statWriteDDL emblemstats icons 127 purchased 1", "statWriteDDL emblemstats icons 127 unlocked 1", "statWriteDDL emblemstats icons 128 purchased 1", "statWriteDDL emblemstats icons 128 unlocked 1",
		"statWriteDDL emblemstats icons 129 purchased 1", "statWriteDDL emblemstats icons 129 unlocked 1", "statWriteDDL emblemstats icons 130 purchased 1", "statWriteDDL emblemstats icons 130 unlocked 1", "statWriteDDL emblemstats icons 131 purchased 1", "statWriteDDL emblemstats icons 131 unlocked 1", "statWriteDDL emblemstats icons 132 purchased 1", "statWriteDDL emblemstats icons 132 unlocked 1", "statWriteDDL emblemstats icons 133 purchased 1", "statWriteDDL emblemstats icons 133 unlocked 1",
		"statWriteDDL emblemstats icons 134 purchased 1", "statWriteDDL emblemstats icons 134 unlocked 1", "statWriteDDL emblemstats icons 135 purchased 1", "statWriteDDL emblemstats icons 135 unlocked 1", "statWriteDDL emblemstats icons 136 purchased 1", "statWriteDDL emblemstats icons 136 unlocked 1", "statWriteDDL emblemstats icons 137 purchased 1", "statWriteDDL emblemstats icons 137 unlocked 1", "statWriteDDL emblemstats icons 138 purchased 1", "statWriteDDL emblemstats icons 138 unlocked 1",
		"statWriteDDL emblemstats icons 139 purchased 1", "statWriteDDL emblemstats icons 139 unlocked 1", "statWriteDDL emblemstats icons 140 purchased 1", "statWriteDDL emblemstats icons 140 unlocked 1", "statWriteDDL emblemstats icons 141 purchased 1", "statWriteDDL emblemstats icons 141 unlocked 1", "statWriteDDL emblemstats icons 142 purchased 1", "statWriteDDL emblemstats icons 142 unlocked 1", "statWriteDDL emblemstats icons 143 purchased 1", "statWriteDDL emblemstats icons 143 unlocked 1",
		"statWriteDDL emblemstats icons 144 purchased 1", "statWriteDDL emblemstats icons 144 unlocked 1", "statWriteDDL emblemstats icons 145 purchased 1", "statWriteDDL emblemstats icons 145 unlocked 1", "statWriteDDL emblemstats icons 146 purchased 1", "statWriteDDL emblemstats icons 146 unlocked 1", "statWriteDDL emblemstats icons 147 purchased 1", "statWriteDDL emblemstats icons 147 unlocked 1", "statWriteDDL emblemstats icons 148 purchased 1", "statWriteDDL emblemstats icons 148 unlocked 1",
		"statWriteDDL emblemstats icons 149 purchased 1", "statWriteDDL emblemstats icons 149 unlocked 1", "statWriteDDL emblemstats icons 150 purchased 1", "statWriteDDL emblemstats icons 150 unlocked 1", "statWriteDDL emblemstats icons 151 purchased 1", "statWriteDDL emblemstats icons 151 unlocked 1", "statWriteDDL emblemstats icons 152 purchased 1", "statWriteDDL emblemstats icons 152 unlocked 1", "statWriteDDL emblemstats icons 153 purchased 1", "statWriteDDL emblemstats icons 153 unlocked 1",
		"statWriteDDL emblemstats icons 154 purchased 1", "statWriteDDL emblemstats icons 154 unlocked 1", "statWriteDDL emblemstats icons 155 purchased 1", "statWriteDDL emblemstats icons 155 unlocked 1", "statWriteDDL emblemstats icons 156 purchased 1", "statWriteDDL emblemstats icons 156 unlocked 1", "statWriteDDL emblemstats icons 157 purchased 1", "statWriteDDL emblemstats icons 157 unlocked 1", "statWriteDDL emblemstats icons 158 purchased 1", "statWriteDDL emblemstats icons 158 unlocked 1",
		"statWriteDDL emblemstats icons 159 purchased 1", "statWriteDDL emblemstats icons 159 unlocked 1", "statWriteDDL emblemstats icons 160 purchased 1", "statWriteDDL emblemstats icons 160 unlocked 1", "statWriteDDL emblemstats icons 161 purchased 1", "statWriteDDL emblemstats icons 161 unlocked 1", "statWriteDDL emblemstats icons 162 purchased 1", "statWriteDDL emblemstats icons 162 unlocked 1", "statWriteDDL emblemstats icons 163 purchased 1", "statWriteDDL emblemstats icons 163 unlocked 1",
		"statWriteDDL emblemstats icons 164 purchased 1", "statWriteDDL emblemstats icons 164 unlocked 1", "statWriteDDL emblemstats icons 165 purchased 1", "statWriteDDL emblemstats icons 165 unlocked 1", "statWriteDDL emblemstats icons 166 purchased 1", "statWriteDDL emblemstats icons 166 unlocked 1", "statWriteDDL emblemstats icons 167 purchased 1", "statWriteDDL emblemstats icons 167 unlocked 1", "statWriteDDL emblemstats icons 168 purchased 1", "statWriteDDL emblemstats icons 168 unlocked 1",
		"statWriteDDL emblemstats icons 169 purchased 1", "statWriteDDL emblemstats icons 169 unlocked 1", "statWriteDDL emblemstats icons 170 purchased 1", "statWriteDDL emblemstats icons 170 unlocked 1", "statWriteDDL emblemstats icons 171 purchased 1", "statWriteDDL emblemstats icons 171 unlocked 1", "statWriteDDL emblemstats icons 172 purchased 1", "statWriteDDL emblemstats icons 172 unlocked 1", "statWriteDDL emblemstats icons 173 purchased 1", "statWriteDDL emblemstats icons 173 unlocked 1",
		"statWriteDDL emblemstats icons 174 purchased 1", "statWriteDDL emblemstats icons 174 unlocked 1", "statWriteDDL emblemstats icons 175 purchased 1", "statWriteDDL emblemstats icons 175 unlocked 1", "statWriteDDL emblemstats icons 176 purchased 1", "statWriteDDL emblemstats icons 176 unlocked 1", "statWriteDDL emblemstats icons 177 purchased 1", "statWriteDDL emblemstats icons 177 unlocked 1", "statWriteDDL emblemstats icons 178 purchased 1", "statWriteDDL emblemstats icons 178 unlocked 1",
		"statWriteDDL emblemstats icons 179 purchased 1", "statWriteDDL emblemstats icons 179 unlocked 1", "statWriteDDL emblemstats icons 180 purchased 1", "statWriteDDL emblemstats icons 180 unlocked 1", "statWriteDDL emblemstats icons 181 purchased 1", "statWriteDDL emblemstats icons 181 unlocked 1", "statWriteDDL emblemstats icons 182 purchased 1", "statWriteDDL emblemstats icons 182 unlocked 1", "statWriteDDL emblemstats icons 183 purchased 1", "statWriteDDL emblemstats icons 183 unlocked 1",
		"statWriteDDL emblemstats icons 184 purchased 1", "statWriteDDL emblemstats icons 184 unlocked 1", "statWriteDDL emblemstats icons 185 purchased 1", "statWriteDDL emblemstats icons 185 unlocked 1", "statWriteDDL emblemstats icons 186 purchased 1", "statWriteDDL emblemstats icons 186 unlocked 1", "statWriteDDL emblemstats icons 187 purchased 1", "statWriteDDL emblemstats icons 187 unlocked 1", "statWriteDDL emblemstats icons 188 purchased 1", "statWriteDDL emblemstats icons 188 unlocked 1",
		"statWriteDDL emblemstats icons 189 purchased 1", "statWriteDDL emblemstats icons 189 unlocked 1", "statWriteDDL emblemstats icons 190 purchased 1", "statWriteDDL emblemstats icons 190 unlocked 1", "statWriteDDL emblemstats icons 191 purchased 1", "statWriteDDL emblemstats icons 191 unlocked 1", "statWriteDDL emblemstats icons 192 purchased 1", "statWriteDDL emblemstats icons 192 unlocked 1", "statWriteDDL emblemstats icons 193 purchased 1", "statWriteDDL emblemstats icons 193 unlocked 1",
		"statWriteDDL emblemstats icons 194 purchased 1", "statWriteDDL emblemstats icons 194 unlocked 1", "statWriteDDL emblemstats icons 195 purchased 1", "statWriteDDL emblemstats icons 195 unlocked 1", "statWriteDDL emblemstats icons 196 purchased 1", "statWriteDDL emblemstats icons 196 unlocked 1", "statWriteDDL emblemstats icons 197 purchased 1", "statWriteDDL emblemstats icons 197 unlocked 1", "statWriteDDL emblemstats icons 198 purchased 1", "statWriteDDL emblemstats icons 198 unlocked 1",
		"statWriteDDL emblemstats icons 199 purchased 1", "statWriteDDL emblemstats icons 199 unlocked 1", "statWriteDDL emblemstats icons 200 purchased 1", "statWriteDDL emblemstats icons 200 unlocked 1", "statWriteDDL emblemstats icons 201 purchased 1", "statWriteDDL emblemstats icons 201 unlocked 1", "statWriteDDL emblemstats icons 202 purchased 1", "statWriteDDL emblemstats icons 202 unlocked 1", "statWriteDDL emblemstats icons 203 purchased 1", "statWriteDDL emblemstats icons 203 unlocked 1",
		"statWriteDDL emblemstats icons 204 purchased 1", "statWriteDDL emblemstats icons 204 unlocked 1", "statWriteDDL emblemstats icons 205 purchased 1", "statWriteDDL emblemstats icons 205 unlocked 1", "statWriteDDL emblemstats icons 206 purchased 1", "statWriteDDL emblemstats icons 206 unlocked 1", "statWriteDDL emblemstats icons 207 purchased 1", "statWriteDDL emblemstats icons 207 unlocked 1", "statWriteDDL emblemstats icons 208 purchased 1", "statWriteDDL emblemstats icons 208 unlocked 1",
		"statWriteDDL emblemstats icons 209 purchased 1", "statWriteDDL emblemstats icons 209 unlocked 1", "statWriteDDL emblemstats icons 210 purchased 1", "statWriteDDL emblemstats icons 210 unlocked 1", "statWriteDDL emblemstats icons 211 purchased 1", "statWriteDDL emblemstats icons 211 unlocked 1", "statWriteDDL emblemstats icons 212 purchased 1", "statWriteDDL emblemstats icons 212 unlocked 1", "statWriteDDL emblemstats icons 213 purchased 1", "statWriteDDL emblemstats icons 213 unlocked 1",
		"statWriteDDL emblemstats icons 214 purchased 1", "statWriteDDL emblemstats icons 214 unlocked 1", "statWriteDDL emblemstats icons 215 purchased 1", "statWriteDDL emblemstats icons 215 unlocked 1", "statWriteDDL emblemstats icons 216 purchased 1", "statWriteDDL emblemstats icons 216 unlocked 1", "statWriteDDL emblemstats icons 217 purchased 1", "statWriteDDL emblemstats icons 217 unlocked 1", "statWriteDDL emblemstats icons 218 purchased 1", "statWriteDDL emblemstats icons 218 unlocked 1",
		"statWriteDDL emblemstats icons 219 purchased 1", "statWriteDDL emblemstats icons 219 unlocked 1", "statWriteDDL emblemstats icons 220 purchased 1", "statWriteDDL emblemstats icons 220 unlocked 1", "statWriteDDL emblemstats icons 221 purchased 1", "statWriteDDL emblemstats icons 221 unlocked 1", "statWriteDDL emblemstats icons 222 purchased 1", "statWriteDDL emblemstats icons 222 unlocked 1", "statWriteDDL emblemstats icons 223 purchased 1", "statWriteDDL emblemstats icons 223 unlocked 1",
		"statWriteDDL emblemstats icons 224 purchased 1", "statWriteDDL emblemstats icons 224 unlocked 1", "statWriteDDL emblemstats icons 225 purchased 1", "statWriteDDL emblemstats icons 225 unlocked 1", "statWriteDDL emblemstats icons 226 purchased 1", "statWriteDDL emblemstats icons 226 unlocked 1", "statWriteDDL emblemstats icons 227 purchased 1", "statWriteDDL emblemstats icons 227 unlocked 1", "statWriteDDL emblemstats icons 228 purchased 1", "statWriteDDL emblemstats icons 228 unlocked 1",
		"statWriteDDL emblemstats icons 229 purchased 1", "statWriteDDL emblemstats icons 229 unlocked 1", "statWriteDDL emblemstats icons 230 purchased 1", "statWriteDDL emblemstats icons 230 unlocked 1", "statWriteDDL emblemstats icons 231 purchased 1", "statWriteDDL emblemstats icons 231 unlocked 1", "statWriteDDL emblemstats icons 232 purchased 1", "statWriteDDL emblemstats icons 232 unlocked 1", "statWriteDDL emblemstats icons 233 purchased 1", "statWriteDDL emblemstats icons 233 unlocked 1",
		"statWriteDDL emblemstats icons 234 purchased 1", "statWriteDDL emblemstats icons 234 unlocked 1", "statWriteDDL emblemstats icons 235 purchased 1", "statWriteDDL emblemstats icons 235 unlocked 1", "statWriteDDL emblemstats icons 236 purchased 1", "statWriteDDL emblemstats icons 236 unlocked 1", "statWriteDDL emblemstats icons 237 purchased 1", "statWriteDDL emblemstats icons 237 unlocked 1", "statWriteDDL emblemstats icons 238 purchased 1", "statWriteDDL emblemstats icons 238 unlocked 1",
		"statWriteDDL emblemstats icons 239 purchased 1", "statWriteDDL emblemstats icons 239 unlocked 1", "statWriteDDL emblemstats icons 240 purchased 1", "statWriteDDL emblemstats icons 240 unlocked 1", "statWriteDDL emblemstats icons 241 purchased 1", "statWriteDDL emblemstats icons 241 unlocked 1", "statWriteDDL emblemstats icons 242 purchased 1", "statWriteDDL emblemstats icons 242 unlocked 1", "statWriteDDL emblemstats icons 243 purchased 1", "statWriteDDL emblemstats icons 243 unlocked 1",
		"statWriteDDL emblemstats icons 244 purchased 1", "statWriteDDL emblemstats icons 244 unlocked 1", "statWriteDDL emblemstats icons 245 purchased 1", "statWriteDDL emblemstats icons 245 unlocked 1", "statWriteDDL emblemstats icons 246 purchased 1", "statWriteDDL emblemstats icons 246 unlocked 1", "statWriteDDL emblemstats icons 247 purchased 1", "statWriteDDL emblemstats icons 247 unlocked 1", "statWriteDDL emblemstats icons 248 purchased 1", "statWriteDDL emblemstats icons 248 unlocked 1",
		"statWriteDDL emblemstats icons 249 purchased 1", "statWriteDDL emblemstats icons 249 unlocked 1", "statWriteDDL emblemstats icons 250 purchased 1", "statWriteDDL emblemstats icons 250 unlocked 1", "statWriteDDL emblemstats icons 251 purchased 1", "statWriteDDL emblemstats icons 251 unlocked 1", "statWriteDDL emblemstats icons 252 purchased 1", "statWriteDDL emblemstats icons 252 unlocked 1", "statWriteDDL emblemstats icons 253 purchased 1", "statWriteDDL emblemstats icons 253 unlocked 1",
		"statWriteDDL emblemstats icons 254 purchased 1", "statWriteDDL emblemstats icons 254 unlocked 1", "statWriteDDL emblemstats icons 255 purchased 1", "statWriteDDL emblemstats icons 255 unlocked 1", "statWriteDDL emblemstats icons 256 purchased 1", "statWriteDDL emblemstats icons 256 unlocked 1", "statWriteDDL emblemstats icons 257 purchased 1", "statWriteDDL emblemstats icons 257 unlocked 1", "statWriteDDL emblemstats icons 258 purchased 1", "statWriteDDL emblemstats icons 258 unlocked 1",
		"statWriteDDL emblemstats icons 259 purchased 1", "statWriteDDL emblemstats icons 259 unlocked 1", "statWriteDDL emblemstats icons 260 purchased 1", "statWriteDDL emblemstats icons 260 unlocked 1", "statWriteDDL itemstats 2 purchased 1", "statWriteDDL itemstats 2 stats headshots statvalue 100", "statWriteDDL itemstats 2 stats headshots challengevalue 100", "statWriteDDL itemstats 2 stats challenges statvalue 14", "statWriteDDL itemstats 2 stats challenges challengevalue 14", "statWriteDDL itemstats 2 stats challenge1 statvalue 150",
		"statWriteDDL itemstats 2 stats challenge1 challengevalue 150", "statWriteDDL itemstats 2 stats challenge2 statvalue 150", "statWriteDDL itemstats 2 stats challenge2 challengevalue 150", "statWriteDDL itemstats 2 stats challenge3 statvalue 150", "statWriteDDL itemstats 2 stats challenge3 challengevalue 150", "statWriteDDL itemstats 2 stats challenge4 statvalue 150", "statWriteDDL itemstats 2 stats challenge4 challengevalue 150", "statWriteDDL itemstats 2 stats challenge5 statvalue 150", "statWriteDDL itemstats 2 stats challenge5 challengevalue 150", "statWriteDDL itemstats 2 stats challenge6 statvalue 150",
		"statWriteDDL itemstats 2 stats challenge6 challengevalue 150", "statWriteDDL itemstats 2 xp 31500", "statWriteDDL itemstats 3 purchased 1", "statWriteDDL itemstats 3 stats headshots statvalue 100", "statWriteDDL itemstats 3 stats headshots challengevalue 100", "statWriteDDL itemstats 3 stats challenges statvalue 14", "statWriteDDL itemstats 3 stats challenges challengevalue 14", "statWriteDDL itemstats 3 stats challenge1 statvalue 150", "statWriteDDL itemstats 3 stats challenge1 challengevalue 150", "statWriteDDL itemstats 3 stats challenge2 statvalue 150",
		"statWriteDDL itemstats 3 stats challenge2 challengevalue 150", "statWriteDDL itemstats 3 stats challenge3 statvalue 150", "statWriteDDL itemstats 3 stats challenge3 challengevalue 150", "statWriteDDL itemstats 3 stats challenge4 statvalue 150", "statWriteDDL itemstats 3 stats challenge4 challengevalue 150", "statWriteDDL itemstats 3 stats challenge5 statvalue 150", "statWriteDDL itemstats 3 stats challenge5 challengevalue 150", "statWriteDDL itemstats 3 stats challenge6 statvalue 150", "statWriteDDL itemstats 3 stats challenge6 challengevalue 150", "statWriteDDL itemstats 3 xp 31500",
		"statWriteDDL itemstats 4 purchased 1", "statWriteDDL itemstats 4 stats headshots statvalue 100", "statWriteDDL itemstats 4 stats headshots challengevalue 100", "statWriteDDL itemstats 4 stats challenges statvalue 14", "statWriteDDL itemstats 4 stats challenges challengevalue 14", "statWriteDDL itemstats 4 stats challenge1 statvalue 150", "statWriteDDL itemstats 4 stats challenge1 challengevalue 150", "statWriteDDL itemstats 4 stats challenge2 statvalue 150", "statWriteDDL itemstats 4 stats challenge2 challengevalue 150", "statWriteDDL itemstats 4 stats challenge3 statvalue 150",
		"statWriteDDL itemstats 4 stats challenge3 challengevalue 150", "statWriteDDL itemstats 4 stats challenge4 statvalue 150", "statWriteDDL itemstats 4 stats challenge4 challengevalue 150", "statWriteDDL itemstats 4 stats challenge5 statvalue 150", "statWriteDDL itemstats 4 stats challenge5 challengevalue 150", "statWriteDDL itemstats 4 stats challenge6 statvalue 150", "statWriteDDL itemstats 4 stats challenge6 challengevalue 150", "statWriteDDL itemstats 4 xp 31500", "statWriteDDL itemstats 5 purchased 1", "statWriteDDL itemstats 5 stats headshots statvalue 100",
		"statWriteDDL itemstats 5 stats headshots challengevalue 100", "statWriteDDL itemstats 5 stats challenges statvalue 14", "statWriteDDL itemstats 5 stats challenges challengevalue 14", "statWriteDDL itemstats 5 stats challenge1 statvalue 150", "statWriteDDL itemstats 5 stats challenge1 challengevalue 150", "statWriteDDL itemstats 5 stats challenge2 statvalue 150", "statWriteDDL itemstats 5 stats challenge2 challengevalue 150", "statWriteDDL itemstats 5 stats challenge3 statvalue 150", "statWriteDDL itemstats 5 stats challenge3 challengevalue 150", "statWriteDDL itemstats 5 stats challenge4 statvalue 150",
		"statWriteDDL itemstats 5 stats challenge4 challengevalue 150", "statWriteDDL itemstats 5 stats challenge5 statvalue 150", "statWriteDDL itemstats 5 stats challenge5 challengevalue 150", "statWriteDDL itemstats 5 stats challenge6 statvalue 150", "statWriteDDL itemstats 5 stats challenge6 challengevalue 150", "statWriteDDL itemstats 5 xp 24800", "statWriteDDL itemstats 6 purchased 1", "statWriteDDL itemstats 6 stats headshots statvalue 100", "statWriteDDL itemstats 6 stats headshots challengevalue 100", "statWriteDDL itemstats 6 stats challenges statvalue 14",
		"statWriteDDL itemstats 6 stats challenges challengevalue 14", "statWriteDDL itemstats 6 stats challenge1 statvalue 150", "statWriteDDL itemstats 6 stats challenge1 challengevalue 150", "statWriteDDL itemstats 6 stats challenge2 statvalue 150", "statWriteDDL itemstats 6 stats challenge2 challengevalue 150", "statWriteDDL itemstats 6 stats challenge3 statvalue 150", "statWriteDDL itemstats 6 stats challenge3 challengevalue 150", "statWriteDDL itemstats 6 stats challenge4 statvalue 150", "statWriteDDL itemstats 6 stats challenge4 challengevalue 150", "statWriteDDL itemstats 6 stats challenge5 statvalue 150",
		"statWriteDDL itemstats 6 stats challenge5 challengevalue 150", "statWriteDDL itemstats 6 stats challenge6 statvalue 150", "statWriteDDL itemstats 6 stats challenge6 challengevalue 150", "statWriteDDL itemstats 6 xp 31500", "statWriteDDL itemstats 13 purchased 1", "statWriteDDL itemstats 13 stats headshots statvalue 100", "statWriteDDL itemstats 13 stats headshots challengevalue 100", "statWriteDDL itemstats 13 stats challenges statvalue 14", "statWriteDDL itemstats 13 stats challenges challengevalue 14", "statWriteDDL itemstats 13 stats challenge1 statvalue 150",
		"statWriteDDL itemstats 13 stats challenge1 challengevalue 150", "statWriteDDL itemstats 13 stats challenge2 statvalue 150", "statWriteDDL itemstats 13 stats challenge2 challengevalue 150", "statWriteDDL itemstats 13 stats challenge3 statvalue 150", "statWriteDDL itemstats 13 stats challenge3 challengevalue 150", "statWriteDDL itemstats 13 stats challenge4 statvalue 150", "statWriteDDL itemstats 13 stats challenge4 challengevalue 150", "statWriteDDL itemstats 13 stats challenge5 statvalue 150", "statWriteDDL itemstats 13 stats challenge5 challengevalue 150", "statWriteDDL itemstats 13 stats challenge6 statvalue 150",
		"statWriteDDL itemstats 13 stats challenge6 challengevalue 150", "statWriteDDL itemstats 13 xp 45400", "statWriteDDL itemstats 14 purchased 1", "statWriteDDL itemstats 14 stats headshots statvalue 100", "statWriteDDL itemstats 14 stats headshots challengevalue 100", "statWriteDDL itemstats 14 stats challenges statvalue 14", "statWriteDDL itemstats 14 stats challenges challengevalue 14", "statWriteDDL itemstats 14 stats challenge1 statvalue 150", "statWriteDDL itemstats 14 stats challenge1 challengevalue 150", "statWriteDDL itemstats 14 stats challenge2 statvalue 150",
		"statWriteDDL itemstats 14 stats challenge2 challengevalue 150", "statWriteDDL itemstats 14 stats challenge3 statvalue 150", "statWriteDDL itemstats 14 stats challenge3 challengevalue 150", "statWriteDDL itemstats 14 stats challenge4 statvalue 150", "statWriteDDL itemstats 14 stats challenge4 challengevalue 150", "statWriteDDL itemstats 14 stats challenge5 statvalue 150", "statWriteDDL itemstats 14 stats challenge5 challengevalue 150", "statWriteDDL itemstats 14 stats challenge6 statvalue 150", "statWriteDDL itemstats 14 stats challenge6 challengevalue 150", "statWriteDDL itemstats 14 xp 45400",
		"statWriteDDL itemstats 15 purchased 1", "statWriteDDL itemstats 15 stats headshots statvalue 100", "statWriteDDL itemstats 15 stats headshots challengevalue 100", "statWriteDDL itemstats 15 stats challenges statvalue 14", "statWriteDDL itemstats 15 stats challenges challengevalue 14", "statWriteDDL itemstats 15 stats challenge1 statvalue 150", "statWriteDDL itemstats 15 stats challenge1 challengevalue 150", "statWriteDDL itemstats 15 stats challenge2 statvalue 150", "statWriteDDL itemstats 15 stats challenge2 challengevalue 150", "statWriteDDL itemstats 15 stats challenge3 statvalue 150",
		"statWriteDDL itemstats 15 stats challenge3 challengevalue 150", "statWriteDDL itemstats 15 stats challenge4 statvalue 150", "statWriteDDL itemstats 15 stats challenge4 challengevalue 150", "statWriteDDL itemstats 15 stats challenge5 statvalue 150", "statWriteDDL itemstats 15 stats challenge5 challengevalue 150", "statWriteDDL itemstats 15 stats challenge6 statvalue 150", "statWriteDDL itemstats 15 stats challenge6 challengevalue 150", "statWriteDDL itemstats 15 xp 45400", "statWriteDDL itemstats 16 purchased 1", "statWriteDDL itemstats 16 stats headshots statvalue 100",
		"statWriteDDL itemstats 16 stats headshots challengevalue 100", "statWriteDDL itemstats 16 stats challenges statvalue 14", "statWriteDDL itemstats 16 stats challenges challengevalue 14", "statWriteDDL itemstats 16 stats challenge1 statvalue 150", "statWriteDDL itemstats 16 stats challenge1 challengevalue 150", "statWriteDDL itemstats 16 stats challenge2 statvalue 150", "statWriteDDL itemstats 16 stats challenge2 challengevalue 150", "statWriteDDL itemstats 16 stats challenge3 statvalue 150", "statWriteDDL itemstats 16 stats challenge3 challengevalue 150", "statWriteDDL itemstats 16 stats challenge4 statvalue 150",
		"statWriteDDL itemstats 16 stats challenge4 challengevalue 150", "statWriteDDL itemstats 16 stats challenge5 statvalue 150", "statWriteDDL itemstats 16 stats challenge5 challengevalue 150", "statWriteDDL itemstats 16 stats challenge6 statvalue 150", "statWriteDDL itemstats 16 stats challenge6 challengevalue 150", "statWriteDDL itemstats 16 xp 45400", "statWriteDDL itemstats 17 purchased 1", "statWriteDDL itemstats 17 stats headshots statvalue 100", "statWriteDDL itemstats 17 stats headshots challengevalue 100", "statWriteDDL itemstats 17 stats challenges statvalue 14",
		"statWriteDDL itemstats 17 stats challenges challengevalue 14", "statWriteDDL itemstats 17 stats challenge1 statvalue 150", "statWriteDDL itemstats 17 stats challenge1 challengevalue 150", "statWriteDDL itemstats 17 stats challenge2 statvalue 150", "statWriteDDL itemstats 17 stats challenge2 challengevalue 150", "statWriteDDL itemstats 17 stats challenge3 statvalue 150", "statWriteDDL itemstats 17 stats challenge3 challengevalue 150", "statWriteDDL itemstats 17 stats challenge4 statvalue 150", "statWriteDDL itemstats 17 stats challenge4 challengevalue 150", "statWriteDDL itemstats 17 stats challenge5 statvalue 150",
		"statWriteDDL itemstats 17 stats challenge5 challengevalue 150", "statWriteDDL itemstats 17 stats challenge6 statvalue 150", "statWriteDDL itemstats 17 stats challenge6 challengevalue 150", "statWriteDDL itemstats 17 xp 45400", "statWriteDDL itemstats 18 purchased 1", "statWriteDDL itemstats 18 stats headshots statvalue 100", "statWriteDDL itemstats 18 stats headshots challengevalue 100", "statWriteDDL itemstats 18 stats challenges statvalue 14", "statWriteDDL itemstats 18 stats challenges challengevalue 14", "statWriteDDL itemstats 18 stats challenge1 statvalue 150",
		"statWriteDDL itemstats 18 stats challenge1 challengevalue 150", "statWriteDDL itemstats 18 stats challenge2 statvalue 150", "statWriteDDL itemstats 18 stats challenge2 challengevalue 150", "statWriteDDL itemstats 18 stats challenge3 statvalue 150", "statWriteDDL itemstats 18 stats challenge3 challengevalue 150", "statWriteDDL itemstats 18 stats challenge4 statvalue 150", "statWriteDDL itemstats 18 stats challenge4 challengevalue 150", "statWriteDDL itemstats 18 stats challenge5 statvalue 150", "statWriteDDL itemstats 18 stats challenge5 challengevalue 150", "statWriteDDL itemstats 18 stats challenge6 statvalue 150",
		"statWriteDDL itemstats 18 stats challenge6 challengevalue 150", "statWriteDDL itemstats 18 xp 45400", "statWriteDDL itemstats 19 purchased 1", "statWriteDDL itemstats 19 stats headshots statvalue 100", "statWriteDDL itemstats 19 stats headshots challengevalue 100", "statWriteDDL itemstats 19 stats challenges statvalue 14", "statWriteDDL itemstats 19 stats challenges challengevalue 14", "statWriteDDL itemstats 19 stats challenge1 statvalue 150", "statWriteDDL itemstats 19 stats challenge1 challengevalue 150", "statWriteDDL itemstats 19 stats challenge2 statvalue 150",
		"statWriteDDL itemstats 19 stats challenge2 challengevalue 150", "statWriteDDL itemstats 19 stats challenge3 statvalue 150", "statWriteDDL itemstats 19 stats challenge3 challengevalue 150", "statWriteDDL itemstats 19 stats challenge4 statvalue 150", "statWriteDDL itemstats 19 stats challenge4 challengevalue 150", "statWriteDDL itemstats 19 stats challenge5 statvalue 150", "statWriteDDL itemstats 19 stats challenge5 challengevalue 150", "statWriteDDL itemstats 19 stats challenge6 statvalue 150", "statWriteDDL itemstats 19 stats challenge6 challengevalue 150", "statWriteDDL itemstats 19 xp 45400",
		"statWriteDDL itemstats 24 purchased 1", "statWriteDDL itemstats 24 stats headshots statvalue 100", "statWriteDDL itemstats 24 stats headshots challengevalue 100", "statWriteDDL itemstats 24 stats challenges statvalue 14", "statWriteDDL itemstats 24 stats challenges challengevalue 14", "statWriteDDL itemstats 24 stats challenge1 statvalue 150", "statWriteDDL itemstats 24 stats challenge1 challengevalue 150", "statWriteDDL itemstats 24 stats challenge2 statvalue 150", "statWriteDDL itemstats 24 stats challenge2 challengevalue 150", "statWriteDDL itemstats 24 stats challenge3 statvalue 150",
		"statWriteDDL itemstats 24 stats challenge3 challengevalue 150", "statWriteDDL itemstats 24 stats challenge4 statvalue 150", "statWriteDDL itemstats 24 stats challenge4 challengevalue 150", "statWriteDDL itemstats 24 stats challenge5 statvalue 150", "statWriteDDL itemstats 24 stats challenge5 challengevalue 150", "statWriteDDL itemstats 24 stats challenge6 statvalue 150", "statWriteDDL itemstats 24 stats challenge6 challengevalue 150", "statWriteDDL itemstats 24 xp 49800", "statWriteDDL itemstats 25 purchased 1", "statWriteDDL itemstats 25 stats headshots statvalue 100",
		"statWriteDDL itemstats 25 stats headshots challengevalue 100", "statWriteDDL itemstats 25 stats challenges statvalue 14", "statWriteDDL itemstats 25 stats challenges challengevalue 14", "statWriteDDL itemstats 25 stats challenge1 statvalue 150", "statWriteDDL itemstats 25 stats challenge1 challengevalue 150", "statWriteDDL itemstats 25 stats challenge2 statvalue 150", "statWriteDDL itemstats 25 stats challenge2 challengevalue 150", "statWriteDDL itemstats 25 stats challenge3 statvalue 150", "statWriteDDL itemstats 25 stats challenge3 challengevalue 150", "statWriteDDL itemstats 25 stats challenge4 statvalue 150",
		"statWriteDDL itemstats 25 stats challenge4 challengevalue 150", "statWriteDDL itemstats 25 stats challenge5 statvalue 150", "statWriteDDL itemstats 25 stats challenge5 challengevalue 150", "statWriteDDL itemstats 25 stats challenge6 statvalue 150", "statWriteDDL itemstats 25 stats challenge6 challengevalue 150", "statWriteDDL itemstats 25 xp 49800", "statWriteDDL itemstats 26 purchased 1", "statWriteDDL itemstats 26 stats headshots statvalue 100", "statWriteDDL itemstats 26 stats headshots challengevalue 100", "statWriteDDL itemstats 26 stats challenges statvalue 14",
		"statWriteDDL itemstats 26 stats challenges challengevalue 14", "statWriteDDL itemstats 26 stats challenge1 statvalue 150", "statWriteDDL itemstats 26 stats challenge1 challengevalue 150", "statWriteDDL itemstats 26 stats challenge2 statvalue 150", "statWriteDDL itemstats 26 stats challenge2 challengevalue 150", "statWriteDDL itemstats 26 stats challenge3 statvalue 150", "statWriteDDL itemstats 26 stats challenge3 challengevalue 150", "statWriteDDL itemstats 26 stats challenge4 statvalue 150", "statWriteDDL itemstats 26 stats challenge4 challengevalue 150", "statWriteDDL itemstats 26 stats challenge5 statvalue 150",
		"statWriteDDL itemstats 26 stats challenge5 challengevalue 150", "statWriteDDL itemstats 26 stats challenge6 statvalue 150", "statWriteDDL itemstats 26 stats challenge6 challengevalue 150", "statWriteDDL itemstats 26 xp 49800", "statWriteDDL itemstats 27 purchased 1", "statWriteDDL itemstats 27 stats headshots statvalue 100", "statWriteDDL itemstats 27 stats headshots challengevalue 100", "statWriteDDL itemstats 27 stats challenges statvalue 14", "statWriteDDL itemstats 27 stats challenges challengevalue 14", "statWriteDDL itemstats 27 stats challenge1 statvalue 150",
		"statWriteDDL itemstats 27 stats challenge1 challengevalue 150", "statWriteDDL itemstats 27 stats challenge2 statvalue 150", "statWriteDDL itemstats 27 stats challenge2 challengevalue 150", "statWriteDDL itemstats 27 stats challenge3 statvalue 150", "statWriteDDL itemstats 27 stats challenge3 challengevalue 150", "statWriteDDL itemstats 27 stats challenge4 statvalue 150", "statWriteDDL itemstats 27 stats challenge4 challengevalue 150", "statWriteDDL itemstats 27 stats challenge5 statvalue 150", "statWriteDDL itemstats 27 stats challenge5 challengevalue 150", "statWriteDDL itemstats 27 stats challenge6 statvalue 150",
		"statWriteDDL itemstats 27 stats challenge6 challengevalue 150", "statWriteDDL itemstats 27 xp 49800", "statWriteDDL itemstats 28 purchased 1", "statWriteDDL itemstats 28 stats headshots statvalue 100", "statWriteDDL itemstats 28 stats headshots challengevalue 100", "statWriteDDL itemstats 28 stats challenges statvalue 14", "statWriteDDL itemstats 28 stats challenges challengevalue 14", "statWriteDDL itemstats 28 stats challenge1 statvalue 150", "statWriteDDL itemstats 28 stats challenge1 challengevalue 150", "statWriteDDL itemstats 28 stats challenge2 statvalue 150",
		"statWriteDDL itemstats 28 stats challenge2 challengevalue 150", "statWriteDDL itemstats 28 stats challenge3 statvalue 150", "statWriteDDL itemstats 28 stats challenge3 challengevalue 150", "statWriteDDL itemstats 28 stats challenge4 statvalue 150", "statWriteDDL itemstats 28 stats challenge4 challengevalue 150", "statWriteDDL itemstats 28 stats challenge5 statvalue 150", "statWriteDDL itemstats 28 stats challenge5 challengevalue 150", "statWriteDDL itemstats 28 stats challenge6 statvalue 150", "statWriteDDL itemstats 28 stats challenge6 challengevalue 150", "statWriteDDL itemstats 28 xp 49800",
		"statWriteDDL itemstats 29 purchased 1", "statWriteDDL itemstats 29 stats headshots statvalue 100", "statWriteDDL itemstats 29 stats headshots challengevalue 100", "statWriteDDL itemstats 29 stats challenges statvalue 14", "statWriteDDL itemstats 29 stats challenges challengevalue 14", "statWriteDDL itemstats 29 stats challenge1 statvalue 150", "statWriteDDL itemstats 29 stats challenge1 challengevalue 150", "statWriteDDL itemstats 29 stats challenge2 statvalue 150", "statWriteDDL itemstats 29 stats challenge2 challengevalue 150", "statWriteDDL itemstats 29 stats challenge3 statvalue 150",
		"statWriteDDL itemstats 29 stats challenge3 challengevalue 150", "statWriteDDL itemstats 29 stats challenge4 statvalue 150", "statWriteDDL itemstats 29 stats challenge4 challengevalue 150", "statWriteDDL itemstats 29 stats challenge5 statvalue 150", "statWriteDDL itemstats 29 stats challenge5 challengevalue 150", "statWriteDDL itemstats 29 stats challenge6 statvalue 150", "statWriteDDL itemstats 29 stats challenge6 challengevalue 150", "statWriteDDL itemstats 29 xp 49800", "statWriteDDL itemstats 30 purchased 1", "statWriteDDL itemstats 30 stats headshots statvalue 100",
		"statWriteDDL itemstats 30 stats headshots challengevalue 100", "statWriteDDL itemstats 30 stats challenges statvalue 14", "statWriteDDL itemstats 30 stats challenges challengevalue 14", "statWriteDDL itemstats 30 stats challenge1 statvalue 150", "statWriteDDL itemstats 30 stats challenge1 challengevalue 150", "statWriteDDL itemstats 30 stats challenge2 statvalue 150", "statWriteDDL itemstats 30 stats challenge2 challengevalue 150", "statWriteDDL itemstats 30 stats challenge3 statvalue 150", "statWriteDDL itemstats 30 stats challenge3 challengevalue 150", "statWriteDDL itemstats 30 stats challenge4 statvalue 150",
		"statWriteDDL itemstats 30 stats challenge4 challengevalue 150", "statWriteDDL itemstats 30 stats challenge5 statvalue 150", "statWriteDDL itemstats 30 stats challenge5 challengevalue 150", "statWriteDDL itemstats 30 stats challenge6 statvalue 150", "statWriteDDL itemstats 30 stats challenge6 challengevalue 150", "statWriteDDL itemstats 30 xp 49800", "statWriteDDL itemstats 31 purchased 1", "statWriteDDL itemstats 31 stats headshots statvalue 100", "statWriteDDL itemstats 31 stats headshots challengevalue 100", "statWriteDDL itemstats 31 stats challenges statvalue 14",
		"statWriteDDL itemstats 31 stats challenges challengevalue 14", "statWriteDDL itemstats 31 stats challenge1 statvalue 150", "statWriteDDL itemstats 31 stats challenge1 challengevalue 150", "statWriteDDL itemstats 31 stats challenge2 statvalue 150", "statWriteDDL itemstats 31 stats challenge2 challengevalue 150", "statWriteDDL itemstats 31 stats challenge3 statvalue 150", "statWriteDDL itemstats 31 stats challenge3 challengevalue 150", "statWriteDDL itemstats 31 stats challenge4 statvalue 150", "statWriteDDL itemstats 31 stats challenge4 challengevalue 150", "statWriteDDL itemstats 31 stats challenge5 statvalue 150",
		"statWriteDDL itemstats 31 stats challenge5 challengevalue 150", "statWriteDDL itemstats 31 stats challenge6 statvalue 150", "statWriteDDL itemstats 31 stats challenge6 challengevalue 150", "statWriteDDL itemstats 31 xp 49800", "statWriteDDL itemstats 32 purchased 1", "statWriteDDL itemstats 32 stats headshots statvalue 100", "statWriteDDL itemstats 32 stats headshots challengevalue 100", "statWriteDDL itemstats 32 stats challenges statvalue 14", "statWriteDDL itemstats 32 stats challenges challengevalue 14", "statWriteDDL itemstats 32 stats challenge1 statvalue 150",
		"statWriteDDL itemstats 32 stats challenge1 challengevalue 150", "statWriteDDL itemstats 32 stats challenge2 statvalue 150", "statWriteDDL itemstats 32 stats challenge2 challengevalue 150", "statWriteDDL itemstats 32 stats challenge3 statvalue 150", "statWriteDDL itemstats 32 stats challenge3 challengevalue 150", "statWriteDDL itemstats 32 stats challenge4 statvalue 150", "statWriteDDL itemstats 32 stats challenge4 challengevalue 150", "statWriteDDL itemstats 32 stats challenge5 statvalue 150", "statWriteDDL itemstats 32 stats challenge5 challengevalue 150", "statWriteDDL itemstats 32 stats challenge6 statvalue 150",
		"statWriteDDL itemstats 32 stats challenge6 challengevalue 150", "statWriteDDL itemstats 32 xp 49800", "statWriteDDL itemstats 36 purchased 1", "statWriteDDL itemstats 36 stats headshots statvalue 100", "statWriteDDL itemstats 36 stats headshots challengevalue 100", "statWriteDDL itemstats 36 stats challenges statvalue 14", "statWriteDDL itemstats 36 stats challenges challengevalue 14", "statWriteDDL itemstats 36 stats challenge1 statvalue 150", "statWriteDDL itemstats 36 stats challenge1 challengevalue 150", "statWriteDDL itemstats 36 stats challenge2 statvalue 150",
		"statWriteDDL itemstats 36 stats challenge2 challengevalue 150", "statWriteDDL itemstats 36 stats challenge3 statvalue 150", "statWriteDDL itemstats 36 stats challenge3 challengevalue 150", "statWriteDDL itemstats 36 stats challenge4 statvalue 150", "statWriteDDL itemstats 36 stats challenge4 challengevalue 150", "statWriteDDL itemstats 36 stats challenge5 statvalue 150", "statWriteDDL itemstats 36 stats challenge5 challengevalue 150", "statWriteDDL itemstats 36 stats challenge6 statvalue 150", "statWriteDDL itemstats 36 stats challenge6 challengevalue 150", "statWriteDDL itemstats 36 xp 45400",
		"statWriteDDL itemstats 37 purchased 1", "statWriteDDL itemstats 37 stats headshots statvalue 100", "statWriteDDL itemstats 37 stats headshots challengevalue 100", "statWriteDDL itemstats 37 stats challenges statvalue 14", "statWriteDDL itemstats 37 stats challenges challengevalue 14", "statWriteDDL itemstats 37 stats challenge1 statvalue 150", "statWriteDDL itemstats 37 stats challenge1 challengevalue 150", "statWriteDDL itemstats 37 stats challenge2 statvalue 150", "statWriteDDL itemstats 37 stats challenge2 challengevalue 150", "statWriteDDL itemstats 37 stats challenge3 statvalue 150",
		"statWriteDDL itemstats 37 stats challenge3 challengevalue 150", "statWriteDDL itemstats 37 stats challenge4 statvalue 150", "statWriteDDL itemstats 37 stats challenge4 challengevalue 150", "statWriteDDL itemstats 37 stats challenge5 statvalue 150", "statWriteDDL itemstats 37 stats challenge5 challengevalue 150", "statWriteDDL itemstats 37 stats challenge6 statvalue 150", "statWriteDDL itemstats 37 stats challenge6 challengevalue 150", "statWriteDDL itemstats 37 xp 45400", "statWriteDDL itemstats 38 purchased 1", "statWriteDDL itemstats 38 stats headshots statvalue 100",
		"statWriteDDL itemstats 38 stats headshots challengevalue 100", "statWriteDDL itemstats 38 stats challenges statvalue 14", "statWriteDDL itemstats 38 stats challenges challengevalue 14", "statWriteDDL itemstats 38 stats challenge1 statvalue 150", "statWriteDDL itemstats 38 stats challenge1 challengevalue 150", "statWriteDDL itemstats 38 stats challenge2 statvalue 150", "statWriteDDL itemstats 38 stats challenge2 challengevalue 150", "statWriteDDL itemstats 38 stats challenge3 statvalue 150", "statWriteDDL itemstats 38 stats challenge3 challengevalue 150", "statWriteDDL itemstats 38 stats challenge4 statvalue 150",
		"statWriteDDL itemstats 38 stats challenge4 challengevalue 150", "statWriteDDL itemstats 38 stats challenge5 statvalue 150", "statWriteDDL itemstats 38 stats challenge5 challengevalue 150", "statWriteDDL itemstats 38 stats challenge6 statvalue 150", "statWriteDDL itemstats 38 stats challenge6 challengevalue 150", "statWriteDDL itemstats 38 xp 45400", "statWriteDDL itemstats 39 purchased 1", "statWriteDDL itemstats 39 stats headshots statvalue 100", "statWriteDDL itemstats 39 stats headshots challengevalue 100", "statWriteDDL itemstats 39 stats challenges statvalue 14",
		"statWriteDDL itemstats 39 stats challenges challengevalue 14", "statWriteDDL itemstats 39 stats challenge1 statvalue 150", "statWriteDDL itemstats 39 stats challenge1 challengevalue 150", "statWriteDDL itemstats 39 stats challenge2 statvalue 150", "statWriteDDL itemstats 39 stats challenge2 challengevalue 150", "statWriteDDL itemstats 39 stats challenge3 statvalue 150", "statWriteDDL itemstats 39 stats challenge3 challengevalue 150", "statWriteDDL itemstats 39 stats challenge4 statvalue 150", "statWriteDDL itemstats 39 stats challenge4 challengevalue 150", "statWriteDDL itemstats 39 stats challenge5 statvalue 150",
		"statWriteDDL itemstats 39 stats challenge5 challengevalue 150", "statWriteDDL itemstats 39 stats challenge6 statvalue 150", "statWriteDDL itemstats 39 stats challenge6 challengevalue 150", "statWriteDDL itemstats 39 xp 45400", "statWriteDDL itemstats 42 purchased 1", "statWriteDDL itemstats 42 stats challenges statvalue 14", "statWriteDDL itemstats 42 stats challenges challengevalue 14", "statWriteDDL itemstats 42 stats challenge1 statvalue 250", "statWriteDDL itemstats 42 stats challenge1 challengevalue 250", "statWriteDDL itemstats 42 stats challenge2 statvalue 250",
		"statWriteDDL itemstats 42 stats challenge2 challengevalue 250", "statWriteDDL itemstats 42 stats challenge3 statvalue 250", "statWriteDDL itemstats 42 stats challenge3 challengevalue 250", "statWriteDDL itemstats 42 stats challenge4 statvalue 250", "statWriteDDL itemstats 42 stats challenge4 challengevalue 250", "statWriteDDL itemstats 42 stats challenge5 statvalue 250", "statWriteDDL itemstats 42 stats challenge5 challengevalue 250", "statWriteDDL itemstats 42 stats challenge6 statvalue 250", "statWriteDDL itemstats 42 stats challenge6 challengevalue 250", "statWriteDDL itemstats 42 xp 39000",
		"statWriteDDL itemstats 43 purchased 1", "statWriteDDL itemstats 43 stats challenges statvalue 14", "statWriteDDL itemstats 43 stats challenges challengevalue 14", "statWriteDDL itemstats 43 stats challenge1 statvalue 250", "statWriteDDL itemstats 43 stats challenge1 challengevalue 250", "statWriteDDL itemstats 43 stats challenge2 statvalue 250", "statWriteDDL itemstats 43 stats challenge2 challengevalue 250", "statWriteDDL itemstats 43 stats challenge3 statvalue 250", "statWriteDDL itemstats 43 stats challenge3 challengevalue 250", "statWriteDDL itemstats 43 stats challenge4 statvalue 250",
		"statWriteDDL itemstats 43 stats challenge4 challengevalue 250", "statWriteDDL itemstats 43 stats challenge5 statvalue 250", "statWriteDDL itemstats 43 stats challenge5 challengevalue 250", "statWriteDDL itemstats 43 stats challenge6 statvalue 250", "statWriteDDL itemstats 43 stats challenge6 challengevalue 250", "statWriteDDL itemstats 43 xp 31500", "statWriteDDL itemstats 44 purchased 1", "statWriteDDL itemstats 44 stats challenges statvalue 14", "statWriteDDL itemstats 44 stats challenges challengevalue 14", "statWriteDDL itemstats 44 stats challenge1 statvalue 250",
		"statWriteDDL itemstats 44 stats challenge1 challengevalue 250", "statWriteDDL itemstats 44 stats challenge2 statvalue 250", "statWriteDDL itemstats 44 stats challenge2 challengevalue 250", "statWriteDDL itemstats 44 stats challenge3 statvalue 250", "statWriteDDL itemstats 44 stats challenge3 challengevalue 250", "statWriteDDL itemstats 44 stats challenge4 statvalue 250", "statWriteDDL itemstats 44 stats challenge4 challengevalue 250", "statWriteDDL itemstats 44 stats challenge5 statvalue 250", "statWriteDDL itemstats 44 stats challenge5 challengevalue 250", "statWriteDDL itemstats 44 stats challenge6 statvalue 250",
		"statWriteDDL itemstats 44 stats challenge6 challengevalue 250", "statWriteDDL itemstats 44 xp 31500", "statWriteDDL itemstats 45 purchased 1", "statWriteDDL itemstats 45 stats challenges statvalue 14", "statWriteDDL itemstats 45 stats challenges challengevalue 14", "statWriteDDL itemstats 45 stats challenge1 statvalue 250", "statWriteDDL itemstats 45 stats challenge1 challengevalue 250", "statWriteDDL itemstats 45 stats challenge2 statvalue 250", "statWriteDDL itemstats 45 stats challenge2 challengevalue 250", "statWriteDDL itemstats 45 stats challenge3 statvalue 250",
		"statWriteDDL itemstats 45 stats challenge3 challengevalue 250", "statWriteDDL itemstats 45 stats challenge4 statvalue 250", "statWriteDDL itemstats 45 stats challenge4 challengevalue 250", "statWriteDDL itemstats 45 stats challenge5 statvalue 250", "statWriteDDL itemstats 45 stats challenge5 challengevalue 250", "statWriteDDL itemstats 45 stats challenge6 statvalue 250", "statWriteDDL itemstats 45 stats challenge6 challengevalue 250", "statWriteDDL itemstats 45 xp 31500", "statWriteDDL itemstats 47 purchased 1", "statWriteDDL itemstats 47 stats challenges statvalue 14",
		"statWriteDDL itemstats 47 stats challenges challengevalue 14", "statWriteDDL itemstats 47 stats challenge1 statvalue 250", "statWriteDDL itemstats 47 stats challenge1 challengevalue 250", "statWriteDDL itemstats 47 stats challenge2 statvalue 250", "statWriteDDL itemstats 47 stats challenge2 challengevalue 250", "statWriteDDL itemstats 47 stats challenge3 statvalue 250", "statWriteDDL itemstats 47 stats challenge3 challengevalue 250", "statWriteDDL itemstats 47 stats challenge4 statvalue 250", "statWriteDDL itemstats 47 stats challenge4 challengevalue 250", "statWriteDDL itemstats 47 stats challenge5 statvalue 250",
		"statWriteDDL itemstats 47 stats challenge5 challengevalue 250", "statWriteDDL itemstats 47 stats challenge6 statvalue 250", "statWriteDDL itemstats 47 stats challenge6 challengevalue 250", "statWriteDDL itemstats 47 xp 31500", "statWriteDDL itemstats 48 purchased 1", "statWriteDDL itemstats 48 stats challenges statvalue 14", "statWriteDDL itemstats 48 stats challenges challengevalue 14", "statWriteDDL itemstats 48 stats challenge1 statvalue 250", "statWriteDDL itemstats 48 stats challenge1 challengevalue 250", "statWriteDDL itemstats 48 stats challenge2 statvalue 250",
		"statWriteDDL itemstats 48 stats challenge2 challengevalue 250", "statWriteDDL itemstats 48 stats challenge3 statvalue 250", "statWriteDDL itemstats 48 stats challenge3 challengevalue 250", "statWriteDDL itemstats 48 stats challenge4 statvalue 250", "statWriteDDL itemstats 48 stats challenge4 challengevalue 250", "statWriteDDL itemstats 48 stats challenge5 statvalue 250", "statWriteDDL itemstats 48 stats challenge5 challengevalue 250", "statWriteDDL itemstats 48 stats challenge6 statvalue 250", "statWriteDDL itemstats 48 stats challenge6 challengevalue 250", "statWriteDDL itemstats 48 xp 31500",
		"statWriteDDL itemstats 49 purchased 1", "statWriteDDL itemstats 49 stats challenges statvalue 14", "statWriteDDL itemstats 49 stats challenges challengevalue 14", "statWriteDDL itemstats 49 stats challenge1 statvalue 250", "statWriteDDL itemstats 49 stats challenge1 challengevalue 250", "statWriteDDL itemstats 49 stats challenge2 statvalue 250", "statWriteDDL itemstats 49 stats challenge2 challengevalue 250", "statWriteDDL itemstats 49 stats challenge3 statvalue 250", "statWriteDDL itemstats 49 stats challenge3 challengevalue 250", "statWriteDDL itemstats 49 stats challenge4 statvalue 250",
		"statWriteDDL itemstats 49 stats challenge4 challengevalue 250", "statWriteDDL itemstats 49 stats challenge5 statvalue 250", "statWriteDDL itemstats 49 stats challenge5 challengevalue 250", "statWriteDDL itemstats 49 stats challenge6 statvalue 250", "statWriteDDL itemstats 49 stats challenge6 challengevalue 250", "statWriteDDL itemstats 49 xp 31500", "statWriteDDL itemstats 50 purchased 1", "statWriteDDL itemstats 50 stats challenges statvalue 14", "statWriteDDL itemstats 50 stats challenges challengevalue 14", "statWriteDDL itemstats 50 stats challenge1 statvalue 250",
		"statWriteDDL itemstats 50 stats challenge1 challengevalue 250", "statWriteDDL itemstats 50 stats challenge2 statvalue 250", "statWriteDDL itemstats 50 stats challenge2 challengevalue 250", "statWriteDDL itemstats 50 stats challenge3 statvalue 250", "statWriteDDL itemstats 50 stats challenge3 challengevalue 250", "statWriteDDL itemstats 50 stats challenge4 statvalue 250", "statWriteDDL itemstats 50 stats challenge4 challengevalue 250", "statWriteDDL itemstats 50 stats challenge5 statvalue 250", "statWriteDDL itemstats 50 stats challenge5 challengevalue 250", "statWriteDDL itemstats 50 stats challenge6 statvalue 250",
		"statWriteDDL itemstats 50 stats challenge6 challengevalue 250", "statWriteDDL itemstats 50 xp 31500", "statWriteDDL itemstats 53 purchased 1", "statWriteDDL itemstats 53 stats challenges statvalue 14", "statWriteDDL itemstats 53 stats challenges challengevalue 14", "statWriteDDL itemstats 53 stats challenge1 statvalue 100", "statWriteDDL itemstats 53 stats challenge1 challengevalue 100", "statWriteDDL itemstats 53 stats challenge2 statvalue 100", "statWriteDDL itemstats 53 stats challenge2 challengevalue 100", "statWriteDDL itemstats 53 stats challenge3 statvalue 100",
		"statWriteDDL itemstats 53 stats challenge3 challengevalue 100", "statWriteDDL itemstats 53 stats challenge4 statvalue 100", "statWriteDDL itemstats 53 stats challenge4 challengevalue 100", "statWriteDDL itemstats 53 stats challenge5 statvalue 100", "statWriteDDL itemstats 53 stats challenge5 challengevalue 100", "statWriteDDL itemstats 53 stats challenge6 statvalue 100", "statWriteDDL itemstats 53 stats challenge6 challengevalue 100", "statWriteDDL itemstats 53 xp 9500", "statWriteDDL itemstats 54 purchased 1", "statWriteDDL itemstats 54 stats challenges statvalue 14",
		"statWriteDDL itemstats 54 stats challenges challengevalue 14", "statWriteDDL itemstats 54 stats challenge1 statvalue 100", "statWriteDDL itemstats 54 stats challenge1 challengevalue 100", "statWriteDDL itemstats 54 stats challenge2 statvalue 100", "statWriteDDL itemstats 54 stats challenge2 challengevalue 100", "statWriteDDL itemstats 54 stats challenge3 statvalue 100", "statWriteDDL itemstats 54 stats challenge3 challengevalue 100", "statWriteDDL itemstats 54 stats challenge4 statvalue 100", "statWriteDDL itemstats 54 stats challenge4 challengevalue 100", "statWriteDDL itemstats 54 stats challenge5 statvalue 100",
		"statWriteDDL itemstats 54 stats challenge5 challengevalue 100", "statWriteDDL itemstats 54 stats challenge6 statvalue 100", "statWriteDDL itemstats 54 stats challenge6 challengevalue 100", "statWriteDDL itemstats 54 xp 9500", "statWriteDDL itemstats 55 purchased 1", "statWriteDDL itemstats 55 stats kills statvalue 100", "statWriteDDL itemstats 55 stats kills challengevalue 100", "statWriteDDL itemstats 55 stats challenges statvalue 14", "statWriteDDL itemstats 55 stats challenges challengevalue 14", "statWriteDDL itemstats 55 stats challenge1 statvalue 10",
		"statWriteDDL itemstats 55 stats challenge1 challengevalue 10", "statWriteDDL itemstats 55 stats challenge2 statvalue 10", "statWriteDDL itemstats 55 stats challenge2 challengevalue 10", "statWriteDDL itemstats 55 stats challenge3 statvalue 10", "statWriteDDL itemstats 55 stats challenge3 challengevalue 10", "statWriteDDL itemstats 55 stats challenge4 statvalue 10", "statWriteDDL itemstats 55 stats challenge4 challengevalue 10", "statWriteDDL itemstats 55 stats challenge5 statvalue 10", "statWriteDDL itemstats 55 stats challenge5 challengevalue 10", "statWriteDDL itemstats 55 stats challenge6 statvalue 10",
		"statWriteDDL itemstats 55 stats challenge6 challengevalue 10", "statWriteDDL itemstats 55 xp 9500", "statWriteDDL itemstats 57 purchased 1", "statWriteDDL itemstats 57 stats kills statvalue 100", "statWriteDDL itemstats 57 stats kills challengevalue 100", "statWriteDDL itemstats 57 stats challenges statvalue 14", "statWriteDDL itemstats 57 stats challenges challengevalue 14", "statWriteDDL itemstats 57 stats challenge1 statvalue 1000", "statWriteDDL itemstats 57 stats challenge1 challengevalue 1000", "statWriteDDL itemstats 57 stats challenge2 statvalue 1000",
		"statWriteDDL itemstats 57 stats challenge2 challengevalue 1000", "statWriteDDL itemstats 57 stats challenge3 statvalue 1000", "statWriteDDL itemstats 57 stats challenge3 challengevalue 1000", "statWriteDDL itemstats 57 stats challenge4 statvalue 1000", "statWriteDDL itemstats 57 stats challenge4 challengevalue 1000", "statWriteDDL itemstats 57 stats challenge5 statvalue 1000", "statWriteDDL itemstats 57 stats challenge5 challengevalue 1000", "statWriteDDL itemstats 57 stats challenge6 statvalue 1000", "statWriteDDL itemstats 57 stats challenge6 challengevalue 1000", "statWriteDDL itemstats 57 xp 9500",
		"statWriteDDL itemstats 58 purchased 1", "statWriteDDL itemstats 58 stats challenges statvalue 14", "statWriteDDL itemstats 58 stats challenges challengevalue 14", "statWriteDDL itemstats 58 stats kills statvalue 300", "statWriteDDL itemstats 58 stats kills challengevalue 300", "statWriteDDL itemstats 58 stats challenge1 statvalue 5", "statWriteDDL itemstats 58 stats challenge1 challengevalue 5", "statWriteDDL itemstats 58 stats challenge2 statvalue 5", "statWriteDDL itemstats 58 stats challenge2 challengevalue 5", "statWriteDDL itemstats 58 stats challenge3 statvalue 5",
		"statWriteDDL itemstats 58 stats challenge3 challengevalue 5", "statWriteDDL itemstats 58 stats challenge4 statvalue 5", "statWriteDDL itemstats 58 stats challenge4 challengevalue 5", "statWriteDDL itemstats 58 stats challenge5 statvalue 5", "statWriteDDL itemstats 58 stats challenge5 challengevalue 5", "statWriteDDL itemstats 58 stats challenge6 statvalue 5", "statWriteDDL itemstats 58 stats challenge6 challengevalue 5", "statWriteDDL itemstats 58 xp 9500", "statWriteDDL itemstats 59 purchased 1", "statWriteDDL itemstats 59 stats challenges statvalue 14",
		"statWriteDDL itemstats 59 stats challenges challengevalue 14", "statWriteDDL itemstats 59 stats challenge1 statvalue 300", "statWriteDDL itemstats 59 stats challenge1 challengevalue 300", "statWriteDDL itemstats 59 stats challenge2 statvalue 300", "statWriteDDL itemstats 59 stats challenge2 challengevalue 300", "statWriteDDL itemstats 59 stats challenge3 statvalue 300", "statWriteDDL itemstats 59 stats challenge3 challengevalue 300", "statWriteDDL itemstats 59 stats challenge4 statvalue 300", "statWriteDDL itemstats 59 stats challenge4 challengevalue 300", "statWriteDDL itemstats 59 stats challenge5 statvalue 300",
		"statWriteDDL itemstats 59 stats challenge5 challengevalue 300", "statWriteDDL itemstats 59 stats challenge6 statvalue 300", "statWriteDDL itemstats 59 stats challenge6 challengevalue 300", "statWriteDDL itemstats 59 xp 9500", "statWriteDDL itemstats 81 purchased 1", "statWriteDDL itemstats 81 stats challenges statvalue 14", "statWriteDDL itemstats 81 stats challenges challengevalue 14", "statWriteDDL itemstats 81 stats kills statvalue 200", "statWriteDDL itemstats 81 stats kills challengevalue 200", "statWriteDDL itemstats 81 stats challenge1 statvalue 10",
		"statWriteDDL itemstats 81 stats challenge1 challengevalue 10", "statWriteDDL itemstats 81 stats challenge2 statvalue 10", "statWriteDDL itemstats 81 stats challenge2 challengevalue 10", "statWriteDDL itemstats 81 stats challenge3 statvalue 10", "statWriteDDL itemstats 81 stats challenge3 challengevalue 10", "statWriteDDL itemstats 81 stats challenge4 statvalue 10", "statWriteDDL itemstats 81 stats challenge4 challengevalue 10", "statWriteDDL itemstats 81 stats challenge5 statvalue 10", "statWriteDDL itemstats 81 stats challenge5 challengevalue 10", "statWriteDDL itemstats 81 stats challenge6 statvalue 10",
		"statWriteDDL itemstats 81 stats challenge6 challengevalue 10", "statWriteDDL groupstats weapon_assault stats challenges statvalue 135", "statWriteDDL groupstats weapon_assault stats challenges challengevalue 135", "statWriteDDL groupstats weapon_pistol stats challenges statvalue 75", "statWriteDDL groupstats weapon_pistol stats challenges challengevalue 75", "statWriteDDL groupstats weapon_smg stats challenges statvalue 90", "statWriteDDL groupstats weapon_smg stats challenges challengevalue 90", "statWriteDDL groupstats weapon_lmg stats challenges statvalue 60", "statWriteDDL groupstats weapon_lmg stats challenges challengevalue 60", "statWriteDDL groupstats weapon_sniper stats challenges statvalue 60",
		"statWriteDDL groupstats weapon_sniper stats challenges challengevalue 60", "statWriteDDL groupstats weapon_cqb stats challenges statvalue 60", "statWriteDDL groupstats weapon_cqb stats challenges challengevalue 60", "statWriteDDL groupstats weapon_launcher stats challenges statvalue 45", "statWriteDDL groupstats weapon_launcher stats challenges challengevalue 45", "statWriteDDL groupstats weapon_special stats challenges statvalue 60", "statWriteDDL groupstats weapon_special stats challenges challengevalue 60"
	};

	private static readonly Dictionary<int, string> WeaponNames = new Dictionary<int, string>
	{
		[2] = "KAP-40",
		[3] = "TAC-45",
		[4] = "B23R",
		[5] = "Executioner",
		[6] = "Five-seven",
		[13] = "MP7",
		[14] = "Skorpion",
		[15] = "PDW-57",
		[16] = "Chicom CQB",
		[17] = "MSMC",
		[18] = "Vector",
		[19] = "Peacekeeper",
		[24] = "M8A1",
		[25] = "SCAR-H",
		[26] = "AN-94",
		[27] = "SWAT-556",
		[28] = "Type 25",
		[29] = "FAL OSW",
		[30] = "SMR",
		[31] = "M27",
		[32] = "MTAR",
		[36] = "Mk 48",
		[37] = "QBB LSW",
		[38] = "LSAT",
		[39] = "HAMR",
		[42] = "Ballista",
		[43] = "SVU-AS",
		[44] = "DSR 50",
		[45] = "XPR-50",
		[47] = "R870 MCS",
		[48] = "M1216",
		[49] = "S12",
		[50] = "KSG",
		[53] = "SMAW",
		[54] = "FHJ-18 AA",
		[55] = "RPG",
		[57] = "Riot Shield",
		[58] = "Item 58",
		[59] = "Ballistic Knife"
	};

	private static readonly Dictionary<int, int> WeaponMaxXp = new Dictionary<int, int>
	{
		[2] = 31500,
		[3] = 31500,
		[4] = 31500,
		[5] = 24800,
		[6] = 31500,
		[13] = 45400,
		[14] = 45400,
		[15] = 45400,
		[16] = 45400,
		[17] = 45400,
		[18] = 45400,
		[19] = 45400,
		[24] = 49800,
		[25] = 49800,
		[26] = 49800,
		[27] = 49800,
		[28] = 49800,
		[29] = 49800,
		[30] = 49800,
		[31] = 49800,
		[32] = 49800,
		[36] = 45400,
		[37] = 45400,
		[38] = 45400,
		[39] = 45400,
		[42] = 39000,
		[43] = 31500,
		[44] = 31500,
		[45] = 31500,
		[47] = 31500,
		[48] = 31500,
		[49] = 31500,
		[50] = 31500,
		[53] = 9500,
		[54] = 9500,
		[55] = 9500,
		[57] = 9500,
		[58] = 9500,
		[59] = 9500
	};

	private static readonly string[] RecoveredMedalFields = new string[138]
	{
		"medal_aitank_kill", "medal_assisted_suicide", "medal_backstabber_kill", "medal_ballistic_knife_kill", "medal_bomb_detonated", "medal_bounce_hatchet_kill", "medal_capture_enemy_crate", "medal_clear_2_attackers", "medal_comeback_from_deathstreak", "medal_completed_match",
		"medal_crossbow_kill", "medal_death_machine_kill", "medal_defend_hq_last_alive", "medal_defused_bomb", "medal_defused_bomb_last_man_alive", "medal_destroyed_aitank", "medal_destroyed_counteruav", "medal_destroyed_heli_comlink", "medal_destroyed_heli_guard", "medal_destroyed_heli_gunner",
		"medal_destroyed_microwave_turret", "medal_destroyed_missile_drone", "medal_destroyed_missile_swarm", "medal_destroyed_plane_mortar", "medal_destroyed_qrdrone", "medal_destroyed_rcbomb", "medal_destroyed_remote_missle", "medal_destroyed_remote_mortar", "medal_destroyed_sentry_gun", "medal_destroyed_straferun",
		"medal_destroyed_supply_drop", "medal_destroyed_uav", "medal_dogs_kill", "medal_eliminate_oic", "medal_eliminate_sd", "medal_elimination_and_last_player_alive", "medal_final_kill_elimination", "medal_first_kill", "medal_flag_capture", "medal_flag_carrier_kill_return_close",
		"medal_hack_3_agrs_in_hack", "medal_hacked", "medal_hatchet_kill", "medal_headshot", "medal_helicopter_comlink_kill", "medal_helicopter_guard_kill", "medal_helicopter_gunner_kill", "medal_hq_destroyed", "medal_humiliation_gun", "medal_kill_confirmed_multi",
		"medal_kill_enemies_one_bullet", "medal_kill_enemy_after_death", "medal_kill_enemy_injuring_teammate", "medal_kill_enemy_one_bullet", "medal_kill_enemy_recent_dive_prone", "medal_kill_enemy_when_injured", "medal_kill_enemy_while_capping", "medal_kill_enemy_who_killed_teammate", "medal_kill_enemy_with_care_package_crush", "medal_kill_enemy_with_hacked_care_package",
		"medal_kill_enemy_with_more_ammo_oic", "medal_kill_enemy_with_their_weapon", "medal_kill_flag_carrier", "medal_kill_hacker_in_hack", "medal_kill_hacker_then_hack_in_hack", "medal_kill_in_3_seconds_gun", "medal_kill_leader_with_axe_sas", "medal_kill_with_axe_sas", "medal_kill_with_crossbow_and_ballistic_sas", "medal_kill_x2_score_shrp",
		"medal_killed_bomb_defuser", "medal_killed_bomb_planter", "medal_killed_enemy_while_carrying_flag", "medal_killstreak_10", "medal_killstreak_15", "medal_killstreak_20", "medal_killstreak_25", "medal_killstreak_30", "medal_killstreak_5", "medal_killstreak_more_than_30",
		"medal_knife_leader_gun", "medal_knife_with_ammo_oic", "medal_koth_secure", "medal_longshot_kill", "medal_melee_kill_with_riot_shield", "medal_microwave_turret_kill", "medal_missile_drone_kill", "medal_missile_swarm_kill", "medal_most_points_shrp", "medal_multikill_2",
		"medal_multikill_3", "medal_multikill_4", "medal_multikill_5", "medal_multikill_6", "medal_multikill_7", "medal_multikill_8", "medal_multikill_more_than_8", "medal_multiple_grenade_launcher_kill", "medal_neutral_b_secured", "medal_plane_mortar_kill",
		"medal_position_secure", "medal_qrdrone_kill", "medal_quickly_secure_point", "medal_rcxd_kill", "medal_remote_missile_kill", "medal_remote_mortar_kill", "medal_retrieve_own_tags", "medal_revenge_kill", "medal_sentry_gun_kill", "medal_share_package_aitank",
		"medal_share_package_ammo", "medal_share_package_counter_uav", "medal_share_package_death_machine", "medal_share_package_dogs", "medal_share_package_emp", "medal_share_package_helicopter_comlink", "medal_share_package_helicopter_guard", "medal_share_package_helicopter_gunner", "medal_share_package_microwave_turret", "medal_share_package_missile_drone",
		"medal_share_package_missle_swarm", "medal_share_package_multiple_grenade_launcher", "medal_share_package_plane_mortar", "medal_share_package_qrdrone", "medal_share_package_rcbomb", "medal_share_package_remote_missile", "medal_share_package_remote_mortar", "medal_share_package_satellite", "medal_share_package_sentry_gun", "medal_share_package_strafe_run",
		"medal_share_package_uav", "medal_stick_explosive_kill", "medal_stop_enemy_killstreak", "medal_straff_run_kill", "medal_teammate_confirm_kill", "medal_uninterrupted_obit_feed_kills", "medal_won_match", "medal_x2_score_shrp"
	};

	private static readonly string[] ClassColorCodes = new string[9] { "^1", "^2", "^3", "^4", "^5", "^6", "^7", "^8", "^9" };

	private readonly Random classColorRandom = new Random();

	private static readonly Dictionary<string, int> AdvancedWeaponIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
	{
		["TAC-45"] = 3,
		["Executioner"] = 5,
		["Ballista"] = 42,
		["SVU-AS"] = 43,
		["DSR-50"] = 44,
		["XPR-50"] = 45,
		["Riot Shield"] = 57,
		["Crossbow"] = 58,
		["Ballistic Knife"] = 59
	};

	private static readonly Dictionary<string, int> AdvancedAttachmentIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
	{
		["None"] = 0,
		["Suppressor"] = 1,
		["Ballistics CPU"] = 2,
		["Zoom Scope"] = 3,
		["Fast Mag"] = 4,
		["FMJ"] = 5,
		["ACOG"] = 6,
		["Extended Clip"] = 7,
		["Laser Sight"] = 8,
		["Dual Band"] = 9
	};

	private static readonly Dictionary<string, int> AdvancedWildcardIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
	{
		["None"] = 0,
		["Primary Gunfighter"] = 178,
		["Secondary Gunfighter"] = 179,
		["Overkill"] = 180,
		["Perk-1 Greed"] = 181,
		["Perk-2 Greed"] = 182,
		["Perk-3 Greed"] = 183,
		["Danger Close"] = 184,
		["Tactician"] = 185
	};

	private int trophyTriggerCounter;

	private bool clientMapRunning;

	private readonly Dictionary<string, Dictionary<ulong, byte[]>> mapWatch = new Dictionary<string, Dictionary<ulong, byte[]>>();

	private DateTime lastQuiet = DateTime.MinValue;

	private readonly Dictionary<string, List<string>> preparedRecoveryWrappers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

	private int nextWeaponRecoveryIndex;

	private readonly Dictionary<string, int> safeBatchResume = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	private static readonly Color CleanBg = Color.FromArgb(18, 18, 22);

	private static readonly Color CleanSurface = Color.FromArgb(24, 24, 30);

	private static readonly Color CleanSurfaceHover = Color.FromArgb(30, 30, 38);

	private static readonly Color CleanSurfaceActive = Color.FromArgb(36, 36, 46);

	private static readonly Color CleanBorder = Color.FromArgb(48, 48, 60);

	private static readonly Color CleanBorderBright = Color.FromArgb(72, 72, 92);

	private static readonly Color CleanAccent = Color.FromArgb(96, 165, 250);

	private static readonly Color CleanAccentDim = Color.FromArgb(59, 130, 246);

	private static readonly Color CleanAccentSoft = Color.FromArgb(96, 165, 250, 30);

	private static readonly Color CleanText = Color.FromArgb(235, 235, 245);

	private static readonly Color CleanTextDim = Color.FromArgb(150, 150, 165);

	private static readonly Color CleanTextMuted = Color.FromArgb(100, 100, 120);

	private static readonly Color CleanSuccess = Color.FromArgb(34, 197, 94);

	private static readonly Color CleanWarning = Color.FromArgb(251, 191, 36);

	private static readonly Color CleanError = Color.FromArgb(239, 68, 68);

	private static readonly Color Bo2Black = Color.FromArgb(18, 18, 22);

	private static readonly Color Bo2Panel = Color.FromArgb(24, 24, 30);

	private static readonly Color Bo2Orange = Color.FromArgb(96, 165, 250);

	private static readonly Color Bo2White = Color.FromArgb(235, 235, 245);

	private static readonly Color Bo2Input = Color.FromArgb(30, 30, 38);

	private static readonly Color Bo2Border = Color.FromArgb(48, 48, 60);

	private static readonly StringFormat FmtCenter = new StringFormat
	{
		Alignment = StringAlignment.Center,
		LineAlignment = StringAlignment.Center
	};

	private static readonly StringFormat FmtCenterNW = new StringFormat
	{
		Alignment = StringAlignment.Center,
		LineAlignment = StringAlignment.Center,
		FormatFlags = StringFormatFlags.NoWrap
	};

	private static readonly StringFormat FmtLeft = new StringFormat
	{
		Alignment = StringAlignment.Near,
		LineAlignment = StringAlignment.Center
	};

	private static readonly StringFormat FmtLeftTrim = new StringFormat
	{
		Alignment = StringAlignment.Near,
		LineAlignment = StringAlignment.Center,
		Trimming = StringTrimming.EllipsisCharacter,
		FormatFlags = StringFormatFlags.NoWrap
	};

	private static readonly Font FontTab9 = new Font("Segoe UI", 9f);

	private static readonly Font FontTab9B = new Font("Segoe UI", 9f, FontStyle.Bold);

	private static readonly Font FontStep8B = new Font("Segoe UI", 8f, FontStyle.Bold);

	private static readonly Font FontBody9 = new Font("Segoe UI", 9f);

	private ulong ControlledAddress => Convert.ToUInt64((controlledCandidate.SelectedItem?.ToString() ?? "0x2A994C2").Substring(2), 16);

	private bool ControlledUsesSmallStates
	{
		get
		{
			if (ControlledAddress != 21586608)
			{
				return ControlledAddress == 21586612;
			}
			return true;
		}
	}

	private byte ControlledLowValue
	{
		get
		{
			if (!ControlledUsesSmallStates)
			{
				return 32;
			}
			return 1;
		}
	}

	private byte ControlledHighValue
	{
		get
		{
			if (!ControlledUsesSmallStates)
			{
				return 64;
			}
			return 2;
		}
	}

	public MainForm()
	{
		InitializeClickSound();
		string text = ModeSelectionForm.ChooseMode();
		zombiesMode = !StandaloneGscOptionsUpdating && text == "Zombies";
		Text = "BO2 TOOL @wyzyxc";
		base.Icon = Program.LoadAppIcon();
		base.Width = 1240;
		base.Height = 780;
		MinimumSize = new Size(1080, 640);
		base.StartPosition = FormStartPosition.CenterScreen;
		BackColor = CleanBg;
		DoubleBuffered = true;
		Font = new Font("Segoe UI", 9.5f);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 4,
			BackColor = CleanBg,
			Margin = Padding.Empty,
			Padding = Padding.Empty
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 86f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
		base.Controls.Add(tableLayoutPanel);
		Panel titlePanel = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = CleanBg,
			Margin = Padding.Empty
		};
		Label value = new Label
		{
			Text = "RTM TOOL BO2   @wyzyxc",
			Font = new Font("Segoe UI", 12f, FontStyle.Bold),
			ForeColor = CleanText,
			AutoSize = false,
			TextAlign = ContentAlignment.MiddleCenter,
			Dock = DockStyle.Fill,
			BackColor = Color.Transparent
		};
		Label modeTag = new Label
		{
			Text = (zombiesMode ? "ZOMBIES" : "MULTIPLAYER"),
			Font = new Font("Segoe UI", 8f, FontStyle.Bold),
			ForeColor = CleanAccent,
			AutoSize = false,
			Size = new Size(120, 24),
			TextAlign = ContentAlignment.MiddleCenter,
			BackColor = CleanBg
		};
		titlePanel.Controls.Add(value);
		titlePanel.Controls.Add(modeTag);
		updatesButton = new Button
		{
			Text = "CHECK UPDATES",
			Font = new Font("Segoe UI", 8f, FontStyle.Bold),
			ForeColor = CleanAccent,
			BackColor = CleanSurface,
			FlatStyle = FlatStyle.Flat,
			Size = new Size(140, 26),
			Cursor = Cursors.Hand,
			Anchor = (AnchorStyles.Top | AnchorStyles.Right)
		};
		updatesButton.FlatAppearance.BorderColor = CleanBorder;
		updatesButton.FlatAppearance.BorderSize = 1;
		updatesButton.Click += async delegate
		{
			await CheckForUpdatesAsync(showNoUpdate: true);
		};
		titlePanel.Controls.Add(updatesButton);
		titlePanel.Resize += delegate
		{
			PlaceHeaderControls();
		};
		PlaceHeaderControls();
		tableLayoutPanel.Controls.Add(titlePanel, 0, 0);
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1,
			BackColor = CleanBg,
			Margin = new Padding(18, 2, 18, 10)
		};
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		CleanModeButton control = new CleanModeButton("MULTIPLAYER", !zombiesMode)
		{
			Dock = DockStyle.Fill,
			Margin = new Padding(0, 0, 7, 0)
		};
		CleanModeButton control2 = new CleanModeButton(StandaloneGscOptionsUpdating ? "ZOMBIES - UPDATING" : "ZOMBIES", zombiesMode)
		{
			Dock = DockStyle.Fill,
			Margin = new Padding(7, 0, 0, 0),
			Enabled = !StandaloneGscOptionsUpdating
		};
		tableLayoutPanel2.Controls.Add(control, 0, 0);
		tableLayoutPanel2.Controls.Add(control2, 1, 0);
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 1);
		TabControl tabs = new TabControl
		{
			Dock = DockStyle.Fill,
			ItemSize = new Size(1, 1),
			Margin = new Padding(0),
			Padding = new Point(0, 0),
			BackColor = CleanBg
		};
		StyleTabStrip(tabs, 1, 1);
		tabs.TabPages.Add(ConnectionPlayersLobbyTab());
		if (!zombiesMode)
		{
			tabs.TabPages.Add(AccountTab());
			tabs.TabPages.Add(CombinedClassesTab());
			tabs.TabPages.Add(TrophiesTab());
			tabs.TabPages.Add(StatsTab());
			tabs.TabPages.Add(InfectionTab());
			tabs.TabPages.Add(ModMenusTab());
		}
		else
		{
			tabs.TabPages.Add(ZombiesHomeTab());
		}
		tabs.TabPages.Add(DiagnosticsTab());
		tabs.TabPages.Add(InstructionsTab());
		foreach (TabPage tabPage in tabs.TabPages)
		{
			tabPage.BackColor = CleanBg;
			tabPage.Padding = new Padding(0);
			tabPage.Paint += DrawCleanWatermark;
		}
		NavBar nav = new NavBar
		{
			Dock = DockStyle.Fill,
			Height = 40,
			Margin = Padding.Empty
		};
		for (int num = 0; num < tabs.TabCount; num++)
		{
			nav.AddCategory(tabs.TabPages[num].Text);
		}
		nav.CategoryClicked += delegate(int idx)
		{
			if (tabs.SelectedIndex != idx)
			{
				tabs.SelectedIndex = idx;
			}
			else
			{
				nav.SetSelected(idx);
			}
		};
		nav.SetSelected(0);
		tabs.SelectedIndexChanged += delegate
		{
			nav.SetSelected(tabs.SelectedIndex);
		};
		TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 2,
			BackColor = CleanBg,
			Margin = Padding.Empty,
			Padding = Padding.Empty
		};
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel3.Controls.Add(nav, 0, 0);
		tableLayoutPanel3.Controls.Add(tabs, 0, 1);
		tableLayoutPanel.Controls.Add(tableLayoutPanel3, 0, 2);
		StatusStrip statusStrip = new StatusStrip
		{
			Dock = DockStyle.Fill,
			SizingGrip = false,
			BackColor = CleanSurface,
			ForeColor = CleanTextDim,
			Font = new Font("Segoe UI", 8f),
			RenderMode = ToolStripRenderMode.Professional
		};
		statusStrip.Renderer = new CleanStatusStripRenderer();
		modificationStatus.Spring = true;
		modificationStatus.TextAlign = ContentAlignment.MiddleLeft;
		modificationStatus.ForeColor = CleanText;
		modificationStatus.Font = new Font("Segoe UI", 8f);
		statusStrip.Items.Add(modificationStatus);
		statusStrip.Items.Add(new ToolStripStatusLabel("Modification progress")
		{
			ForeColor = CleanTextMuted,
			Font = new Font("Segoe UI", 8f)
		});
		modificationProgress.Width = 220;
		modificationProgress.Maximum = 100;
		modificationProgress.Value = 0;
		modificationProgress.BackColor = CleanBorder;
		modificationProgress.ForeColor = CleanAccent;
		modificationProgress.Style = ProgressBarStyle.Continuous;
		statusStrip.Items.Add(modificationProgress);
		tableLayoutPanel.Controls.Add(statusStrip, 0, 3);
		base.Shown += delegate
		{
			nav.LayoutItems();
		};
		base.Shown += async delegate
		{
			await CheckForUpdatesAsync(showNoUpdate: false);
		};
		base.Resize += delegate
		{
			nav.LayoutItems();
		};
		ApplyCleanTheme(this);
		nav.BackColor = Color.FromArgb(31, 34, 45);
		nav.Invalidate();
		monitor.Tick += delegate
		{
			MonitorTick();
		};
		validatorTimer.Tick += delegate
		{
			ValidatorTick();
		};
		transitionTimer.Tick += delegate
		{
			TransitionTick();
		};
		lanCaptureTimer.Tick += async delegate
		{
			await CaptureLanSampleAsync();
		};
		controlledCandidate.Items.AddRange("0x14962B0", "0x14962B4", "0x2A994C2", "0x2D1FE6B", "0x2D21A9E");
		controlledCandidate.SelectedIndex = 2;
		controlledCandidate.SelectedIndexChanged += delegate
		{
			controlledOriginal = null;
			controlledSavedAddress = null;
			Log($"CW candidate selected: 0x{ControlledAddress:X}. Saved original cleared.");
		};
		players.SelectedIndexChanged += delegate
		{
			CaptureLiveSelectionFromUi();
		};
		HookOptionSounds(this);
		Log("BO2 TOOL @wyzyxc v6.32 started in " + (zombiesMode ? "ZOMBIES" : "MULTIPLAYER") + " mode.");
		Log("Clean UI loaded successfully.");
		void PlaceHeaderControls()
		{
			modeTag.Location = new Point(titlePanel.Width - 130, (titlePanel.Height - 24) / 2);
			updatesButton.Location = new Point(titlePanel.Width - 282, (titlePanel.Height - updatesButton.Height) / 2);
		}
	}

	private async Task CheckForUpdatesAsync(bool showNoUpdate)
	{
		Button button = updatesButton;
		if (button == null || button.IsDisposed)
		{
			return;
		}
		button.Enabled = false;
		string priorText = button.Text;
		try
		{
			button.Text = "CHECKING...";
			UpdateService.UpdateCheck update = await UpdateService.CheckAsync();
			if (!update.Configured)
			{
				button.Text = "CHECK UPDATES";
				if (showNoUpdate)
				{
					MessageBox.Show(this, "Online updates are not configured in this build. Set UpdateManifestUrl when publishing the first update-enabled release.", "Updates not configured", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				}
				return;
			}
			if (!update.Available)
			{
				button.Text = "CHECK UPDATES";
				if (showNoUpdate)
				{
					MessageBox.Show(this, "You have the latest version (" + update.CurrentVersion + ").", "No update available", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				}
				return;
			}
			button.Text = "NEW UPDATE " + update.LatestVersion;
			if (MessageBox.Show(this, $"A signed update is available.\n\nCurrent version: {update.CurrentVersion}\nNew version: {update.LatestVersion}\n\nDownload, verify, and install it now? The tool will close and restart after you confirm.", "New BO2 RTM update", MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk) == DialogResult.Yes)
			{
				button.Text = "DOWNLOADING...";
				Log("UPDATE: downloading signed version " + update.LatestVersion + ".");
				UpdateService.StartConfirmedInstall(await UpdateService.DownloadAndVerifyAsync(update), update.Sha256);
				Log("UPDATE: signature and SHA-256 passed; closing for the confirmed installation.");
				Application.Exit();
			}
		}
		catch (Exception ex)
		{
			button.Text = (priorText.StartsWith("NEW UPDATE", StringComparison.Ordinal) ? priorText : "CHECK UPDATES");
			Log("UPDATE CHECK FAILED: " + ex.GetBaseException().Message);
			if (showNoUpdate)
			{
				MessageBox.Show(this, ex.GetBaseException().Message, "Update check failed", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			}
		}
		finally
		{
			if (!button.IsDisposed)
			{
				button.Enabled = true;
			}
		}
	}

	private TabPage InstructionsTab()
	{
		TabPage tabPage = new TabPage("Instructions");
		RichTextBox value = new RichTextBox
		{
			Dock = DockStyle.Fill,
			ReadOnly = true,
			BackColor = Bo2Black,
			ForeColor = Bo2White,
			BorderStyle = BorderStyle.None,
			Font = new Font("Segoe UI", 10f),
			Text = "BO2 RTM V6.28 - QUICK GUIDE\r\n\r\n1. CONNECT\r\nLoad your compatible debug payload on the jailbroken PS4, launch BO2 Multiplayer, enter the LAN lobby, enter the PS4 IP, then Connect / Attach BO2.\r\n\r\n2. SELECT THE PLAYER\r\nOpen Players and press Refresh Players. Select the exact live player you want to edit. Re-refresh whenever somebody leaves or rejoins. Never continue a write if the log says the selected player is no longer resolved.\r\n\r\n3. 100% RECOVERY\r\nOn Account / Recovery, use V6.19 100% Recovery. It applies the currently proven recovery categories: Prestige Master + Level 55, Weapons/Camos, max weapon levels, Calling Cards/Emblems, and baseline normal combat stats. The failed 32-byte unlock routine is not used.\r\n\r\n4. SAVE AND VERIFY\r\nAfter a recovery/edit finishes, press Save Selected Player Profile. Have the edited retail player leave the LAN lobby and rejoin before verifying persistence.\r\n\r\n5. STATS / MEDALS\r\nUse individual Apply buttons for manual career values. Normal Stats applies the baseline recovery preset. For game-mode stats, choose the mode (for example tdm), stat (wins/kills/etc.), enter a value, press Apply Mode Stat, then Save and leave/rejoin.\r\n\r\n6. CLASSES\r\nApply Class Name edits one class. Apply Name/Color to ALL 10 writes the chosen name/color to class slots 1-10. Unlock 10 Classes runs the repaired five-entry EXTRA_CAC path (prestige entries 1-5), saves the selected profile, then requires leave/rejoin verification.\r\n\r\n7. WEAPONS / UNLOCKS\r\nThe category-specific Weapons+Camos and Calling Cards/Emblems routines are the verified unlock paths. Do not treat the old 32-byte recovery block as Full Unlock.\r\n\r\n8. TROPHIES\r\nTrophy and GSC controls are temporarily disabled while this tool is being updated. The Trophies tab shows the current status.\r\n\r\n9. TROUBLESHOOTING\r\nPASS means the command was dispatched; persistence must still be verified after Save + leave/rejoin. If player resolution fails, Refresh Players and reselect the target. If BO2 disconnects or restarts, reconnect/attach before continuing. If BO2 crashes, stop that test and preserve the Diagnostics log.\r\n\r\nRECOMMENDED ORDER\r\nConnect -> Refresh Players -> Select target -> Run one operation -> Save -> target leaves/rejoins -> verify -> continue."
		};
		tabPage.Controls.Add(value);
		return tabPage;
	}

	private TabPage ZombiesHomeTab()
	{
		TabPage tabPage = new TabPage("Zombies");
		FlowLayoutPanel flowLayoutPanel = Stack();
		flowLayoutPanel.Controls.Add(Note("ZOMBIES WORKSPACE — mode-specific controls are kept separate from Multiplayer."));
		flowLayoutPanel.Controls.Add(Note("Zombies GSC options have been temporarily removed while they are being updated."));
		tabPage.Controls.Add(flowLayoutPanel);
		return tabPage;
	}

	private void TestZombiesGscInjection()
	{
		if (StandaloneGscOptionsUpdating)
		{
			Log("ZM-GSC UPDATING: GSC injection is temporarily disabled.");
			return;
		}
		if (!zombiesMode)
		{
			Log("ZM-GSC blocked: restart tool and choose Zombies.");
			return;
		}
		if (debugBridge == null || bo2Process == null)
		{
			Log("ZM-GSC blocked: Connect and Attach BO2/Zombies first.");
			return;
		}
		try
		{
			(string, int) tuple = RequireSelectedWritablePlayer();
			Log($"ZM-GSC START selected='{tuple.Item1}' selectedClient={tuple.Item2} target={"maps/mp/gametypes_zm/_clientids.gsc"}");
			byte[] source = ReadEmbeddedAsset("r_6B20D8F5");
			GscInjectResult gscInjectResult = debugBridge.InjectCompiledGsc(bo2Process, source, "fortis_account_menu_zm.gscc", "maps/mp/gametypes_zm/_clientids.gsc", delegate(string m)
			{
				Log(m);
			});
			gscStatus.Text = "ZM GSC injected";
			Log($"ZM-GSC PASS size={gscInjectResult.Size} asset=0x{gscInjectResult.AssetHeader:X} buffer=0x{gscInjectResult.InjectedBuffer:X} checksum=0x{gscInjectResult.StockChecksum:X8}.");
			Log("ZM-GSC DIAGNOSTIC COMPLETE. Injection transport is now past the morning blocker; Bank/Rank behavior can be tested next.");
		}
		catch (Exception ex)
		{
			gscStatus.Text = "ZM inject failed";
			Log("ZM-GSC FAIL - " + ex.GetBaseException().Message);
			Log("ZM-GSC ACTION: preserve Diagnostics. The last GSC-STAGE line identifies the exact failing operation.");
		}
	}

	private TabPage ConnectionPlayersLobbyTab()
	{
		TabPage tabPage = new TabPage("Connection / Players");
		TabControl tabControl = new TabControl
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty
		};
		StyleTabStrip(tabControl, 140, 30);
		tabControl.MouseDown += delegate(object? _, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left && e.Y < 32)
			{
				PlayClickSound();
			}
		};
		tabControl.TabPages.Add(ConnectionTab());
		tabControl.TabPages.Add(PlayersTab());
		tabControl.TabPages.Add(LobbyTab());
		tabPage.Controls.Add(tabControl);
		return tabPage;
	}

	private TabPage CombinedClassesTab()
	{
		TabPage tabPage = new TabPage("Classes");
		TabControl tabControl = new TabControl
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty
		};
		StyleTabStrip(tabControl, 150, 30);
		tabControl.MouseDown += delegate(object? _, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left && e.Y < 32)
			{
				PlayClickSound();
			}
		};
		TabPage tabPage2 = ClassesTab();
		tabPage2.Text = "Standard / Unlocks";
		TabPage tabPage3 = AdvancedClassesTab();
		tabPage3.Text = "Advanced Classes";
		tabControl.TabPages.Add(tabPage2);
		tabControl.TabPages.Add(tabPage3);
		tabPage.Controls.Add(tabControl);
		return tabPage;
	}

	private TabPage ConnectionTab()
	{
		TabPage tabPage = new TabPage("Connection");
		FlowLayoutPanel flowLayoutPanel = Stack();
		flowLayoutPanel.Controls.Add(SectionTitle("CONNECTION", "Establish the session with the PS4 and attach the BO2 process."));
		flowLayoutPanel.Controls.Add(Row(new Label
		{
			Text = "PS4 IP",
			AutoSize = true,
			ForeColor = CleanTextDim,
			Font = new Font("Segoe UI", 9f, FontStyle.Bold),
			Width = 78,
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = new Padding(0, 7, 0, 7)
		}, ipBox, Btn("Connect", async delegate
		{
			await ConnectAsync();
		}), Btn("Disconnect", delegate
		{
			Disconnect();
		}), attachButton = Btn("Attach BO2", async delegate
		{
			await AttachBO2Async();
		}), Btn("Clear Debug", delegate
		{
			log.Clear();
			Log("Debug log cleared.");
		})));
		flowLayoutPanel.Controls.Add(Spacer(14));
		flowLayoutPanel.Controls.Add(SectionTitle("SESSION STATUS", "Live connection and game state."));
		flowLayoutPanel.Controls.Add(StatusRow("PS4", ps4Status));
		flowLayoutPanel.Controls.Add(StatusRow("BO2 Process", bo2Status));
		flowLayoutPanel.Controls.Add(StatusRow("Game Status", gameState));
		flowLayoutPanel.Controls.Add(StatusRow("Active Clients", clientsStatus));
		flowLayoutPanel.Controls.Add(StatusRow("GSC", gscStatus));
		flowLayoutPanel.Controls.Add(Spacer(14));
		flowLayoutPanel.Controls.Add(SectionTitle("QUICK START", "Follow these steps in order."));
		flowLayoutPanel.Controls.Add(BuildStepRow("1", "Load your debug payload on the jailbroken PS4"));
		flowLayoutPanel.Controls.Add(BuildStepRow("2", "Launch Black Ops 2 in Multiplayer mode"));
		flowLayoutPanel.Controls.Add(BuildStepRow("3", "Press Connect, then Attach BO2"));
		flowLayoutPanel.Controls.Add(BuildStepRow("4", "Join a match from the LAN lobby"));
		flowLayoutPanel.Controls.Add(BuildStepRow("5", "Watch Diagnostics to verify the state"));
		flowLayoutPanel.Controls.Add(Spacer(16));
		flowLayoutPanel.Controls.Add(Note("If a write does not persist, read the Diagnostics log: the last GSC-STAGE or LAN-*-FAIL line identifies the exact operation that failed."));
		tabPage.Controls.Add(flowLayoutPanel);
		return tabPage;
	}

	private static Control SectionTitle(string title, string subtitle)
	{
		Panel obj = new Panel
		{
			Height = 36,
			Width = 820,
			BackColor = Color.Transparent,
			Margin = new Padding(0, 2, 0, 10)
		};
		Label value = new Label
		{
			Text = title,
			Font = new Font("Segoe UI", 10f, FontStyle.Bold),
			ForeColor = CleanText,
			AutoSize = false,
			Size = new Size(400, 18),
			Location = new Point(12, 2),
			BackColor = Color.Transparent
		};
		Label value2 = new Label
		{
			Text = subtitle,
			Font = new Font("Segoe UI", 8.5f),
			ForeColor = CleanTextMuted,
			AutoSize = false,
			Size = new Size(700, 16),
			Location = new Point(12, 19),
			BackColor = Color.Transparent
		};
		Panel value3 = new Panel
		{
			Height = 1,
			Width = 796,
			Location = new Point(12, 35),
			BackColor = Color.FromArgb(70, CleanBorder)
		};
		obj.Controls.Add(value);
		obj.Controls.Add(value2);
		obj.Controls.Add(value3);
		return obj;
	}

	private static byte[] ReadEmbeddedAsset(string resourceName)
	{
		using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName) ?? throw new FileNotFoundException("Embedded release asset '" + resourceName + "' was not found.");
		using MemoryStream memoryStream = new MemoryStream();
		stream.CopyTo(memoryStream);
		return memoryStream.ToArray();
	}

	private static Control Spacer(int h)
	{
		return new Panel
		{
			Height = h,
			Width = 1,
			BackColor = Color.Transparent,
			Margin = Padding.Empty
		};
	}

	private static Control BuildStepRow(string num, string text)
	{
		Panel obj = new Panel
		{
			Height = 28,
			Width = 800,
			BackColor = Color.Transparent,
			Margin = new Padding(0, 0, 0, 2)
		};
		Panel panel = new Panel
		{
			Size = new Size(20, 20),
			Location = new Point(12, 4),
			BackColor = Color.FromArgb(30, 41, 59)
		};
		panel.Paint += delegate(object? s, PaintEventArgs e)
		{
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
			using SolidBrush brush = new SolidBrush(Color.FromArgb(30, 41, 59));
			using SolidBrush brush2 = new SolidBrush(CleanAccent);
			e.Graphics.FillEllipse(brush, 0, 0, 20, 20);
			e.Graphics.DrawString(num, FontStep8B, brush2, new Rectangle(0, 0, 20, 20), FmtCenter);
		};
		Label value = new Label
		{
			Text = text,
			AutoSize = false,
			Size = new Size(740, 28),
			Location = new Point(42, 0),
			TextAlign = ContentAlignment.MiddleLeft,
			ForeColor = Color.FromArgb(180, 182, 194),
			Font = new Font("Segoe UI", 9f),
			BackColor = Color.Transparent
		};
		obj.Controls.Add(panel);
		obj.Controls.Add(value);
		return obj;
	}

	private TabPage PlayersTab()
	{
		TabPage tabPage = new TabPage("Players");
		SplitContainer splitContainer = new SplitContainer
		{
			Dock = DockStyle.Fill,
			SplitterDistance = 350
		};
		for (int i = 0; i < 18; i++)
		{
			players.Items.Add($"Client {i}: <not read>");
		}
		splitContainer.Panel1.Controls.Add(players);
		FlowLayoutPanel flowLayoutPanel = Stack();
		flowLayoutPanel.Controls.Add(Btn("Refresh Players", delegate
		{
			RefreshPlayersStub();
		}));
		flowLayoutPanel.Controls.Add(selectedPlayerDetails);
		flowLayoutPanel.Controls.Add(new Label
		{
			Text = "Verified LAN resolver is active automatically. Select a live player, then use Account or Classes for profile edits.",
			AutoSize = true,
			MaximumSize = new Size(480, 0)
		});
		splitContainer.Panel2.Controls.Add(flowLayoutPanel);
		tabPage.Controls.Add(splitContainer);
		return tabPage;
	}

	private TabPage AccountTab()
	{
		TabPage tabPage = new TabPage("Account / Recovery");
		FlowLayoutPanel flowLayoutPanel = Stack();
		NumericUpDown prestige = new NumericUpDown
		{
			Minimum = 0m,
			Maximum = 2000000000m,
			Value = 11m,
			Width = 140,
			ThousandsSeparator = true,
			TextAlign = HorizontalAlignment.Right
		};
		NumericUpDown rank = new NumericUpDown
		{
			Minimum = 1m,
			Maximum = 55m,
			Value = 55m,
			Width = 80
		};
		NumericUpDown rankXp = new NumericUpDown
		{
			Minimum = 0m,
			Maximum = 1249100m,
			Value = 1197300m,
			Width = 120,
			ThousandsSeparator = true
		};
		CheckBox autoXp = new CheckBox
		{
			Text = "Auto XP from Level",
			Checked = true,
			AutoSize = true
		};
		NumericUpDown permanentTokens = new NumericUpDown
		{
			Minimum = 1m,
			Maximum = 19m,
			Value = 2m,
			Width = 80
		};
		TextBox clan = new TextBox
		{
			Text = "^1BO2",
			Width = 160,
			MaxLength = 15,
			PlaceholderText = "^1BO2"
		};
		rank.ValueChanged += delegate
		{
			SyncRankXp();
		};
		autoXp.CheckedChanged += delegate
		{
			if (autoXp.Checked)
			{
				SyncRankXp();
			}
		};
		flowLayoutPanel.Controls.Add(Note("V6 consolidated test build. Auto Level uses the exact BO2 multiplayer XP thresholds; uncheck Auto XP to enter Rank XP manually. BO2 ^ color codes remain supported."));
		flowLayoutPanel.Controls.Add(Row(new Label
		{
			Text = "Regular Level",
			AutoSize = true
		}, rank, autoXp, new Label
		{
			Text = "Rank XP",
			AutoSize = true
		}, rankXp, Btn("Apply Level + XP", delegate
		{
			SendRecoveredProfileBatch("LEVEL-XP", new string[2]
			{
				$"statSetByName rank {(int)rank.Value}",
				$"statSetByName rankxp {(int)rankXp.Value}"
			});
		})));
		flowLayoutPanel.Controls.Add(Note("Level 55 starts at 1,197,300 XP. 1,249,100 is the post-Level-55 Prestige threshold. Manual mode permits either value."));
		flowLayoutPanel.Controls.Add(Row(new Label
		{
			Text = "Prestige",
			AutoSize = true
		}, prestige, Btn("Apply Prestige", delegate
		{
			ApplyPrestigeStat(prestige.Value);
		})));
		flowLayoutPanel.Controls.Add(Note("Prestige uses statSetByName + playerstatslist DDL, then updategamerprofile;uploadStats and Save. LEAVE/REJOIN the lobby afterwards so the profile is re-read."));
		flowLayoutPanel.Controls.Add(Row(Btn("Unlock All Level-Gated Items", delegate
		{
			SendUnlockBitfield();
		}), Btn("100% Recovery - Prestige Master + 55", delegate
		{
			RunFullRecoveryProvenPaths();
		})));
		flowLayoutPanel.Controls.Add(Note("Normal token statSetByName writes were removed: live testing did not persist them. V6.8 keeps the proven 32-byte unlocks[] recovery state (byte 0 = 245; bytes 1-31 = 255) for level-gated unlock state."));
		flowLayoutPanel.Controls.Add(Row(new Label
		{
			Text = "Permanent Tokens",
			AutoSize = true
		}, permanentTokens, Btn("Preview Permanent Tokens", delegate
		{
			Log($"PERMANENT-TOKENS family confirmed (prestigeTokens, valid 1-19), but the recovered files do not expose the exact arbitrary-count entry mapping. Requested={(int)permanentTokens.Value}. Nothing sent.");
		}), Btn("Unlock 10 Classes", delegate
		{
			RunTenClassRepairTest();
		})));
		flowLayoutPanel.Controls.Add(Row(new Label
		{
			Text = "Clan Tag / Colored",
			AutoSize = true
		}, clan, Btn("Apply Clan Tag", delegate
		{
			SendRecoveredProfileCommand("CLAN", "statWriteDDL clantagstats clanName " + ValidateSimpleValue(clan.Text));
		})));
		ComboBox weapon = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 190
		};
		foreach (KeyValuePair<int, string> item in WeaponNames.OrderBy<KeyValuePair<int, string>, string>((KeyValuePair<int, string> k) => k.Value))
		{
			weapon.Items.Add(new WeaponChoice(item.Key, item.Value));
		}
		if (weapon.Items.Count > 0)
		{
			weapon.SelectedIndex = 0;
		}
		flowLayoutPanel.Controls.Add(Row(Btn("Preview Calling Cards", delegate
		{
			PreviewPackedRecovery("CALLING-CARDS", CallingCardRecoveryCommands());
		}), Btn("Apply Calling Cards", delegate
		{
			ApplyPreparedRecovery("CALLING-CARDS");
		}), Btn("Preview Weapons + Camos", delegate
		{
			PreviewPackedRecovery("WEAPONS-CAMOS", WeaponCamoRecoveryCommands());
		}), Btn("Apply Weapons + Camos", delegate
		{
			ApplyPreparedRecovery("WEAPONS-CAMOS");
		}), Btn("V6.19 100% Recovery", delegate
		{
			RunFullRecoveryProvenPaths();
		})));
		flowLayoutPanel.Controls.Add(Row(Btn("Recover Next Weapon (Safe Test)", delegate
		{
			RecoverNextWeaponCamoGroup();
		}), Btn("Max ALL Weapon Levels", delegate
		{
			SendRecoveredProfileBatchSafe("WEAPON-MAX-ALL", from k in WeaponMaxXp
				orderby k.Key
				select $"statWriteDDL itemstats {k.Key} xp {k.Value}", 10, 150, 1500, 50);
		})));
		flowLayoutPanel.Controls.Add(Row(new Label
		{
			Text = "Selected Weapon",
			AutoSize = true
		}, weapon, Btn("Recover Selected Weapon + Camos", delegate
		{
			object selectedItem = weapon.SelectedItem;
			WeaponChoice w = selectedItem as WeaponChoice;
			if ((object)w != null)
			{
				SendRecoveredProfileBatchSafe("WEAPON-CAMOS-" + w.Id, RecoveryCommands.Where((string x) => x.StartsWith($"statWriteDDL itemstats {w.Id} ", StringComparison.OrdinalIgnoreCase)), 8, 200, 1500);
			}
		}), Btn("Max Selected Weapon Level", delegate
		{
			if (weapon.SelectedItem is WeaponChoice weaponChoice && WeaponMaxXp.TryGetValue(weaponChoice.Id, out var value))
			{
				SendRecoveredProfileCommand("WEAPON-MAX-" + weaponChoice.Id, $"statWriteDDL itemstats {weaponChoice.Id} xp {value}");
			}
			else
			{
				Log("WEAPON-MAX: no recovered max-XP entry for selected item.");
			}
		})));
		flowLayoutPanel.Controls.Add(Note("V6.19 100% Recovery uses only live-proven categories: Prestige Master + Level 55, Weapons+Camos, Max ALL Weapon Levels, Calling Cards/Emblems, and baseline Normal Stats. The failed 32-byte unlock block is excluded. Unlock 10 Classes uses the live-confirmed five-entry EXTRA_CAC purchase mapping and is included in 100% Recovery; the standalone button remains available."));
		flowLayoutPanel.Controls.Add(Row(Btn("Save Selected Player Profile", delegate
		{
			SaveSelectedPlayerProfile();
		})));
		flowLayoutPanel.Controls.Add(Note("After each isolated test: Save Selected Player Profile, have P2 leave/rejoin, then verify before testing another category."));
		tabPage.Controls.Add(flowLayoutPanel);
		return tabPage;
		void SyncRankXp()
		{
			if (autoXp.Checked)
			{
				int num = (int)rank.Value;
				rankXp.Value = Bo2LevelXp[num];
				Log($"LEVEL-TABLE level={num} => rankXp={Bo2LevelXp[num]:N0}.");
			}
		}
	}

	private TabPage ModMenusTab()
	{
		TabPage tabPage = new TabPage("GSC Injector");
		FlowLayoutPanel flowLayoutPanel = Stack();
		flowLayoutPanel.Controls.Add(SectionTitle("GSC MOD MENU INJECTOR", "Choose a folder containing a compiled BO2 GSC menu, then inject it into Multiplayer."));
		flowLayoutPanel.Controls.Add(Note("Browse a menu folder. If package.json exists, its menu name and asset list are used automatically; otherwise you can inject a compiled GSC file found in the folder."));
		TextBox folderPath = new TextBox
		{
			Width = 520,
			ReadOnly = true,
			PlaceholderText = "Select the folder containing your mod menu"
		};
		ComboBox menuFile = new ComboBox
		{
			Width = 300,
			DropDownStyle = ComboBoxStyle.DropDownList,
			Enabled = false
		};
		Label detectedName = StatusLabel("No menu selected");
		Button button = Btn("Browse", delegate
		{
			using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
			{
				Description = "Choose the folder containing compiled BO2 GSC mod menus",
				UseDescriptionForTitle = true
			};
			if (Directory.Exists(folderPath.Text))
			{
				folderBrowserDialog.SelectedPath = folderPath.Text;
			}
			if (folderBrowserDialog.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			folderPath.Text = folderBrowserDialog.SelectedPath;
			menuFile.Items.Clear();
			try
			{
				ModMenuPackage modMenuPackage = ReadModMenuFolder(folderBrowserDialog.SelectedPath);
				if (modMenuPackage != null)
				{
					menuFile.Items.Add(modMenuPackage);
				}
				menuFile.Enabled = menuFile.Items.Count > 0;
				if (menuFile.Items.Count > 0)
				{
					menuFile.SelectedIndex = 0;
				}
				else
				{
					detectedName.Text = "No compiled GSC/GSCC menu found";
					Log("MOD-MENU: no usable compiled menu found in '" + folderBrowserDialog.SelectedPath + "'.");
				}
			}
			catch (Exception ex)
			{
				menuFile.Enabled = false;
				detectedName.Text = "Menu package could not be read";
				Log("MOD-MENU package error: " + ex.GetBaseException().Message);
			}
		});
		menuFile.SelectedIndexChanged += delegate
		{
			if (menuFile.SelectedItem is ModMenuPackage modMenuPackage)
			{
				detectedName.Text = $"Detected menu: {modMenuPackage.Name} ({modMenuPackage.Assets.Count} asset{((modMenuPackage.Assets.Count == 1) ? "" : "s")})";
			}
		};
		flowLayoutPanel.Controls.Add(Row(new Label
		{
			Text = "Menu folder",
			AutoSize = true,
			Margin = new Padding(0, 8, 8, 0)
		}, folderPath, button));
		flowLayoutPanel.Controls.Add(Row(new Label
		{
			Text = "Mod menu",
			AutoSize = true,
			Margin = new Padding(0, 8, 8, 0)
		}, menuFile));
		flowLayoutPanel.Controls.Add(Row(detectedName));
		flowLayoutPanel.Controls.Add(Row(Btn("Inject Mod Menu", delegate
		{
			if (!(menuFile.SelectedItem is ModMenuPackage modMenuPackage))
			{
				Log("MOD-MENU blocked: browse to a folder and select a compiled menu first.");
			}
			else
			{
				if (debugBridge != null && bo2Process != null)
				{
					try
					{
						if (injectedModMenuAssets.Count > 0)
						{
							Log("MOD-MENU: restoring '" + (injectedModMenuName ?? "previous menu") + "' before injecting another menu.");
							RemoveInjectedModMenu();
						}
						gscStatus.Text = "Injecting mod menu...";
						Log($"MOD-MENU START name='{modMenuPackage.Name}' assets={modMenuPackage.Assets.Count}.");
						foreach (ModMenuAsset asset in modMenuPackage.Assets)
						{
							Log($"MOD-MENU ASSET START target='{asset.Target}' file='{asset.Path}'.");
							GscInjectResult gscInjectResult = debugBridge.InjectCompiledGsc(bo2Process, asset.Path, asset.Target, delegate(string m)
							{
								Log(m);
							});
							injectedModMenuAssets.Add(gscInjectResult);
							Log($"MOD-MENU ASSET PASS target='{asset.Target}' size={gscInjectResult.Size} asset=0x{gscInjectResult.AssetHeader:X} buffer=0x{gscInjectResult.InjectedBuffer:X}.");
						}
						injectedModMenuName = modMenuPackage.Name;
						gscStatus.Text = "Injected: " + modMenuPackage.Name;
						Log($"MOD-MENU PASS name='{modMenuPackage.Name}' assets={modMenuPackage.Assets.Count}.");
						Log("MOD-MENU: loaded package assets into the current Multiplayer session. Whether its menu opens automatically depends on the package entry code.");
						try
						{
							string text = modMenuPackage.Name + " injected. Start a match. @wyzyxc&biqk";
							debugBridge.NotifyConsole(222, text);
							Log("MOD-MENU PS4 notification sent: '" + text + "'.");
							return;
						}
						catch (Exception ex)
						{
							Log("MOD-MENU injected, but the PS4 notification failed: " + ex.GetBaseException().Message);
							return;
						}
					}
					catch (Exception ex2)
					{
						gscStatus.Text = ((injectedModMenuAssets.Count > 0) ? "Menu partly injected; remove to restore" : "Mod menu inject failed");
						Log("MOD-MENU FAIL - " + ex2.GetBaseException().Message);
						return;
					}
				}
				gscStatus.Text = "Attach BO2 first";
				Log("MOD-MENU blocked: Connect and attach BO2 first.");
			}
		})));
		flowLayoutPanel.Controls.Add(Row(Btn("Remove Injected Menu", delegate
		{
			try
			{
				RemoveInjectedModMenu();
			}
			catch (Exception ex)
			{
				gscStatus.Text = "Menu restore failed";
				Log("MOD-MENU RESTORE FAIL - " + ex.GetBaseException().Message);
			}
		})));
		flowLayoutPanel.Controls.Add(StatusRow("GSC", gscStatus));
		tabPage.Controls.Add(flowLayoutPanel);
		return tabPage;
	}

	private static ModMenuPackage? ReadModMenuFolder(string folder)
	{
		string path = Path.Combine(folder, "package.json");
		if (File.Exists(path))
		{
			using (JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(path)))
			{
				JsonElement rootElement = jsonDocument.RootElement;
				JsonElement value;
				string name = (rootElement.TryGetProperty("name", out value) ? (value.GetString() ?? Path.GetFileName(folder)) : Path.GetFileName(folder));
				if (!rootElement.TryGetProperty("assets", out var value2) || value2.ValueKind != JsonValueKind.Array)
				{
					throw new InvalidDataException("package.json must contain an assets array.");
				}
				List<ModMenuAsset> list = new List<ModMenuAsset>();
				foreach (JsonElement item in value2.EnumerateArray())
				{
					if (item.ValueKind == JsonValueKind.String)
					{
						string text = item.GetString()?.Trim().Replace('\\', '/');
						if (!string.IsNullOrWhiteSpace(text))
						{
							string text2 = text.Replace('/', Path.DirectorySeparatorChar);
							string[] source = ((!Path.HasExtension(text2)) ? new string[2]
							{
								Path.GetFullPath(Path.Combine(folder, text2 + ".gsc")),
								Path.GetFullPath(Path.Combine(folder, text2 + ".gscc"))
							} : new string[1] { Path.GetFullPath(Path.Combine(folder, text2)) });
							string rootPath = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
							string text3 = source.FirstOrDefault((string candidate) => candidate.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) && IsCompiled(candidate));
							if (text3 == null)
							{
								throw new InvalidDataException("Compiled asset '" + text + "' was not found or has an invalid GSC header.");
							}
							list.Add(new ModMenuAsset(Path.ChangeExtension(text, null) ?? text, text3));
						}
					}
				}
				if (list.Count == 0)
				{
					throw new InvalidDataException("The package has no compiled assets to inject.");
				}
				return new ModMenuPackage(name, list);
			}
		}
		string[] array = (from path3 in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
			where Path.GetExtension(path3).Equals(".gscc", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path3).Equals(".gsc", StringComparison.OrdinalIgnoreCase)
			select path3).Where(IsCompiled).OrderBy<string, string>(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
		if (array.Length == 0)
		{
			return null;
		}
		string path2 = array[0];
		return new ModMenuPackage(Path.GetFileNameWithoutExtension(path2).Replace('_', ' '), new List<ModMenuAsset>
		{
			new ModMenuAsset("maps/mp/gametypes/_clientids.gsc", path2)
		});
		static bool IsCompiled(string path3)
		{
			try
			{
				using FileStream fileStream = File.OpenRead(path3);
				return fileStream.Length >= 64 && fileStream.ReadByte() == 128 && fileStream.ReadByte() == 71;
			}
			catch
			{
				return false;
			}
		}
	}

	private void RemoveInjectedModMenu()
	{
		if (injectedModMenuAssets.Count == 0)
		{
			gscStatus.Text = "No injected menu tracked";
			Log("MOD-MENU REMOVE: no injected menu is tracked by this session.");
			return;
		}
		if (debugBridge == null || bo2Process == null)
		{
			throw new InvalidOperationException("Connect and attach the same BO2 process before restoring the menu.");
		}
		gscStatus.Text = "Restoring original GSC assets...";
		while (injectedModMenuAssets.Count > 0)
		{
			int index = injectedModMenuAssets.Count - 1;
			GscInjectResult gscInjectResult = injectedModMenuAssets[index];
			Log("MOD-MENU RESTORE START target='" + gscInjectResult.Target + "'.");
			debugBridge.RestoreCompiledGsc(bo2Process, gscInjectResult, delegate(string m)
			{
				Log(m);
			});
			injectedModMenuAssets.RemoveAt(index);
			Log("MOD-MENU RESTORE PASS target='" + gscInjectResult.Target + "'.");
		}
		string text = injectedModMenuName;
		injectedModMenuName = null;
		gscStatus.Text = "Original GSC assets restored";
		Log("MOD-MENU REMOVE PASS name='" + (text ?? "tracked menu") + "'. Original assets restored; you can now inject another menu.");
	}

	private TabPage InfectionTab()
	{
		TabPage tabPage = new TabPage("Infection");
		FlowLayoutPanel flowLayoutPanel = Stack();
		flowLayoutPanel.Controls.Add(SectionTitle("INFECTION", "Lobby-switch infection options. Requires attached PS4 and selected player."));
		Label bigNamesInfState = new Label
		{
			Text = "IDLE",
			AutoSize = true,
			ForeColor = CleanTextMuted
		};
		Button button = Btn("Big Names", delegate
		{
			SendBigNamesInfectionChain(bigNamesInfState);
		});
		flowLayoutPanel.Controls.Add(Row(button, bigNamesInfState));
		Label superSpeedState = new Label
		{
			Text = "IDLE",
			AutoSize = true,
			ForeColor = CleanTextMuted
		};
		Button button2 = Btn("Super Speed", delegate
		{
			SendSuperSpeedChain(superSpeedState);
		});
		flowLayoutPanel.Controls.Add(Row(button2, superSpeedState));
		Label forceHostState = new Label
		{
			Text = "IDLE",
			AutoSize = true,
			ForeColor = CleanTextMuted
		};
		Button button3 = Btn("Force Host ON", delegate
		{
			SendForceHostChain(forceHostState);
		});
		flowLayoutPanel.Controls.Add(Row(button3, forceHostState));
		TrackBar fovValue = new TrackBar
		{
			Minimum = 65,
			Maximum = 160,
			Value = 90,
			TickFrequency = 5,
			SmallChange = 1,
			LargeChange = 5,
			Width = 260,
			Height = 42,
			AutoSize = false,
			BackColor = CleanBg
		};
		Label fovValueLabel = new Label
		{
			Text = fovValue.Value.ToString(),
			AutoSize = true,
			ForeColor = CleanAccent,
			Font = new Font("Segoe UI", 11f, FontStyle.Bold),
			Margin = new Padding(4, 10, 12, 0)
		};
		fovValue.ValueChanged += delegate
		{
			fovValueLabel.Text = fovValue.Value.ToString();
		};
		Label fovState = new Label
		{
			Text = "IDLE",
			AutoSize = true,
			ForeColor = CleanTextMuted
		};
		flowLayoutPanel.Controls.Add(Row(new Label
		{
			Text = "FOV",
			AutoSize = true,
			Margin = new Padding(0, 12, 4, 0)
		}, fovValue, fovValueLabel, Btn("Apply FOV", delegate
		{
			SendFovChain(fovValue.Value, fovState);
		}), fovState));
		Label espState = new Label
		{
			Text = "DISABLED",
			AutoSize = true,
			ForeColor = CleanWarning
		};
		Button button4 = Btn("ESP - WORKING", delegate
		{
			SendEspChain(espState);
		});
		button4.Enabled = false;
		flowLayoutPanel.Controls.Add(Row(button4, espState));
		Label uavState = new Label
		{
			Text = "IDLE",
			AutoSize = true,
			ForeColor = CleanTextMuted
		};
		Button button5 = Btn("UAV Constante - WORKING", delegate
		{
			SendUavChain(uavState);
		});
		button5.Enabled = false;
		flowLayoutPanel.Controls.Add(Row(button5, uavState));
		Label fastRadarState = new Label
		{
			Text = "IDLE",
			AutoSize = true,
			ForeColor = CleanTextMuted
		};
		Button button6 = Btn("FAST RADAR - WORKING", delegate
		{
			SendFastRadar(fastRadarState);
		});
		button6.Enabled = false;
		flowLayoutPanel.Controls.Add(Row(button6, fastRadarState));
		flowLayoutPanel.Controls.Add(Row(Btn("Crasher Players", delegate
		{
			SendRecoveredProfileCommand("CLAN", "statWriteDDL clantagstats clanName " + ValidateSimpleValue("^H{}"));
		})));
		Label heavyAimState = new Label
		{
			Text = "IDLE",
			AutoSize = true,
			ForeColor = CleanTextMuted
		};
		Button button7 = Btn("Max Aim Assist - WORKING", async delegate
		{
			await SendHeavyAimAssist(heavyAimState);
		});
		button7.Enabled = false;
		flowLayoutPanel.Controls.Add(Row(button7, heavyAimState));
		Label zeroRecoilState = new Label
		{
			Text = "IDLE",
			AutoSize = true,
			ForeColor = CleanTextMuted
		};
		Button button8 = Btn("0 Recoil - WORKING", delegate
		{
			SendZeroRecoilTest(zeroRecoilState);
		});
		button8.Enabled = false;
		flowLayoutPanel.Controls.Add(Row(button8, zeroRecoilState));
		tabPage.Controls.Add(flowLayoutPanel);
		return tabPage;
	}

	private TabPage StatsTab()
	{
		TabPage tabPage = new TabPage("Stats / Medals");
		FlowLayoutPanel flowLayoutPanel = Stack();
		flowLayoutPanel.Controls.Add(Note("V6.8: basic career stats and medals remain on their live-tested paths. Game-mode stats are isolated below for retest: these counters and medals live under playerstatslist. The failed playerstatsbygametype medal path from V6.5.1 has been removed."));
		NumericUpDown kills = StatNumber(0m, 9999999m);
		NumericUpDown deaths = StatNumber(0m, 9999999m);
		NumericUpDown assists = StatNumber(0m, 9999999m);
		NumericUpDown headshots = StatNumber(0m, 9999999m);
		NumericUpDown wins = StatNumber(0m, 9999999m);
		NumericUpDown losses = StatNumber(0m, 9999999m);
		NumericUpDown score = StatNumber(0m, 999999999m);
		flowLayoutPanel.Controls.Add(Row(new Label
		{
			Text = "Kills",
			AutoSize = true
		}, kills, Btn("Apply", delegate
		{
			SendCareerStat("kills", kills.Value);
		}), new Label
		{
			Text = "Deaths",
			AutoSize = true
		}, deaths, Btn("Apply", delegate
		{
			SendCareerStat("deaths", deaths.Value);
		})));
		flowLayoutPanel.Controls.Add(Row(new Label
		{
			Text = "Assists",
			AutoSize = true
		}, assists, Btn("Apply", delegate
		{
			SendCareerStat("assists", assists.Value);
		}), new Label
		{
			Text = "Headshots",
			AutoSize = true
		}, headshots, Btn("Apply", delegate
		{
			SendCareerStat("headshots", headshots.Value);
		})));
		flowLayoutPanel.Controls.Add(Row(new Label
		{
			Text = "Wins",
			AutoSize = true
		}, wins, Btn("Apply", delegate
		{
			SendCareerStat("wins", wins.Value);
		}), new Label
		{
			Text = "Losses",
			AutoSize = true
		}, losses, Btn("Apply", delegate
		{
			SendCareerStat("losses", losses.Value);
		})));
		flowLayoutPanel.Controls.Add(Row(new Label
		{
			Text = "Score",
			AutoSize = true
		}, score, Btn("Apply", delegate
		{
			SendCareerStat("score", score.Value);
		})));
		flowLayoutPanel.Controls.Add(Row(Btn("Normal Stats", delegate
		{
			ApplyNormalStatsPreset();
		})));
		flowLayoutPanel.Controls.Add(Note("V6.32 Normal Stats applies Career + all 22 supplied Game Mode wins + the supplied Combat medal preset + Scorestreak medal preset. Medal counters write BOTH StatValue and ChallengeValue."));
		NumericUpDown days = StatNumber(0m, 9999m);
		NumericUpDown hours = StatNumber(0m, 23m);
		NumericUpDown minutes = StatNumber(0m, 59m);
		flowLayoutPanel.Controls.Add(Row(new Label
		{
			Text = "Time Played - Days",
			AutoSize = true
		}, days, new Label
		{
			Text = "Hours",
			AutoSize = true
		}, hours, new Label
		{
			Text = "Minutes",
			AutoSize = true
		}, minutes, Btn("Apply Time", delegate
		{
			SendCareerStat("time_played_total", days.Value * 86400m + hours.Value * 3600m + minutes.Value * 60m);
		})));
		ComboBox advField = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 210
		};
		advField.Items.AddRange("killsconfirmed", "killsdenied", "wins_hc", "wins_multiteam", "career_score", "career_score_hc", "career_score_multiteam", "time_played_alive");
		advField.SelectedIndex = 0;
		NumericUpDown advValue = StatNumber(0m, 999999999m);
		flowLayoutPanel.Controls.Add(Row(new Label
		{
			Text = "Recovered counter",
			AutoSize = true
		}, advField, advValue, Btn("Apply Counter", delegate
		{
			SendCareerStat(advField.Text, advValue.Value);
		})));
		ComboBox gameMode = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 100
		};
		gameMode.Items.AddRange("tdm", "dm", "dom", "sd", "ctf", "koth", "dem", "conf");
		gameMode.SelectedIndex = 0;
		ComboBox gameField = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 170
		};
		gameField.Items.AddRange("kills", "deaths", "wins", "losses", "score", "time_played_total", "killsconfirmed", "killsdenied");
		gameField.SelectedIndex = 0;
		NumericUpDown gameValue = StatNumber(0m, 999999999m);
		flowLayoutPanel.Controls.Add(Row(new Label
		{
			Text = "Game Mode (retest)",
			AutoSize = true
		}, gameMode, gameField, gameValue, Btn("Apply Mode Stat", delegate
		{
			SendGameTypeStat(gameMode.Text, gameField.Text, gameValue.Value);
		}), Btn("TDM Kills Probe", delegate
		{
			SendGameTypeStat("tdm", "kills", gameValue.Value);
		})));
		flowLayoutPanel.Controls.Add(Note("V6.16: recovered core-mode table is tdm, dm, dom, sd, ctf, koth, dem, conf (HQ removed). TDM Kills Probe sends only playerstatsbygametype tdm kills statValue <value>; Save separately, leave/rejoin, then verify persistence."));
		ComboBox medalField = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 330
		};
		string[] recoveredMedalFields = RecoveredMedalFields;
		foreach (string item in recoveredMedalFields)
		{
			medalField.Items.Add(item);
		}
		if (medalField.Items.Count > 0)
		{
			medalField.SelectedIndex = 0;
		}
		NumericUpDown medalCount = StatNumber(0m, 9999999m);
		flowLayoutPanel.Controls.Add(Row(new Label
		{
			Text = "Medal",
			AutoSize = true
		}, medalField, new Label
		{
			Text = "Count",
			AutoSize = true
		}, medalCount, Btn("Apply Medal", delegate
		{
			SendCareerStat(medalField.Text, medalCount.Value);
		})));
		flowLayoutPanel.Controls.Add(Note("Medal fields are recovered playerstatslist medal_* paths. Medals remain on the live-tested playerstatslist medal_* path."));
		flowLayoutPanel.Controls.Add(Note("statSetByName commands: plevel, rank, rankxp (PLEVEL is a command name, NOT an offset)."));
		NumericUpDown plevelValue = new NumericUpDown
		{
			Minimum = 0m,
			Maximum = 2000000000m,
			Value = 11m,
			Width = 140,
			ThousandsSeparator = true,
			TextAlign = HorizontalAlignment.Right
		};
		flowLayoutPanel.Controls.Add(Row(new Label
		{
			Text = "PLevel (statSetByName)",
			AutoSize = true
		}, plevelValue, Btn("Apply PLevel", delegate
		{
			ApplyPrestigeStat(plevelValue.Value);
		})));
		NumericUpDown rankValue = StatNumber(0m, 100000m);
		flowLayoutPanel.Controls.Add(Row(new Label
		{
			Text = "Rank (statSetByName)",
			AutoSize = true
		}, rankValue, Btn("Apply Rank", delegate
		{
			SendStatByName("rank", rankValue.Value);
		})));
		NumericUpDown rankXpValue = StatNumber(0m, 999999999m);
		flowLayoutPanel.Controls.Add(Row(new Label
		{
			Text = "Rank XP (statSetByName)",
			AutoSize = true
		}, rankXpValue, Btn("Apply Rank XP", delegate
		{
			SendStatByName("rankxp", rankXpValue.Value);
		})));
		flowLayoutPanel.Controls.Add(Row(Btn("Save Selected Player Profile", delegate
		{
			SaveSelectedPlayerProfile();
		})));
		tabPage.Controls.Add(flowLayoutPanel);
		return tabPage;
	}

	private static NumericUpDown StatNumber(decimal min, decimal max)
	{
		return new NumericUpDown
		{
			Minimum = min,
			Maximum = max,
			Width = 110,
			ThousandsSeparator = true
		};
	}

	private void ApplyPrestigeStat(decimal value)
	{
		try
		{
			(string, int) tuple = RequireSelectedWritablePlayer();
			long value2 = (long)value;
			Log($"PRESTIGE START target='{tuple.Item1}' selectedClient={tuple.Item2} value={value2}.");
			SendRecoveredProfileBatch("PRESTIGE-SETBYNAME", new string[1] { $"statSetByName plevel {value2}" });
			SendRecoveredProfileCommand("PRESTIGE-COMMIT", "updategamerprofile;uploadStats");
			ResponsiveValidationDelay(900);
			SendRecoveredProfileCommand("PRESTIGE-DDL", $"statWriteDDL playerstatslist plevel statvalue {value2};updategamerprofile;uploadStats");
			ResponsiveValidationDelay(900);
			SaveSelectedPlayerProfile();
			MarkModificationApplied("PRESTIGE");
			modificationStatus.Text = $"Prestige {value2} written + committed + saved. LEAVE/REJOIN to see it.";
			Log("PRESTIGE COMPLETE. Prestige written via statSetByName AND playerstatslist DDL, committed and saved.");
			Log("PRESTIGE NEXT: have the player LEAVE and REJOIN the lobby so the profile is re-read, then check the player card.");
		}
		catch (Exception ex)
		{
			Log("PRESTIGE FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void SendLocalDvar(string dvar, string value, Label state, string label, bool useSetCommand = true)
	{
		try
		{
			if (debugBridge == null || bo2Process == null)
			{
				Log(label + " BLOCKED: Connect and attach BO2 first.");
				return;
			}
			string text = (useSetCommand ? ("set " + dvar + " " + value) : (dvar + " " + value));
			Log($"{label} START attachedLocalClient={0} cmd='{text}'. Remote player selection does not affect this command.");
			int num = Encoding.ASCII.GetByteCount(text) + 1;
			if (num > 512)
			{
				throw new InvalidOperationException($"{label}: command exceeds the 512-byte limit ({num}).");
			}
			CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, 0, text);
			Log($"{label} DISPATCH signature={(cbufSendResult.SignatureOk ? "PASS" : "FAIL")} bufferReadback={(cbufSendResult.BufferReadbackOk ? "PASS" : "FAIL")} return=0x{cbufSendResult.ReturnValue:X}.");
			Log(label + " BUFFER '" + cbufSendResult.BufferReadbackAscii + "'.");
			if (cbufSendResult.SignatureOk && cbufSendResult.BufferReadbackOk)
			{
				state.Text = "Dispatched; unverified";
				state.ForeColor = CleanWarning;
				Log($"{label} DISPATCHED to attached console local client {0}: {dvar} = {value}. Signature and buffer readback passed; game acceptance/effect are unverified.");
			}
			else
			{
				state.Text = "Transport failed";
				state.ForeColor = CleanWarning;
				Log(label + " TRANSPORT FAILED.");
			}
		}
		catch (Exception ex)
		{
			state.Text = "Error";
			state.ForeColor = CleanError;
			Log(label + " FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void ApplyInfectionDvar(Label state)
	{
		SendLocalDvar("sv_forcehost", "1", state, "FORCE-HOST");
	}

	private void ApplyFov(int value, Label state)
	{
		SendLocalDvar("cg_fov", value.ToString(), state, "FOV", useSetCommand: false);
	}

	private void ApplyAimAssist(double value, Label state)
	{
		SendLocalDvar("cl_aimassist", value.ToString("0.00", CultureInfo.InvariantCulture), state, "AIM-ASSIST");
	}

	private void SendLocalDvars(string chain, Label state, string label)
	{
		try
		{
			if (debugBridge == null || bo2Process == null)
			{
				Log(label + " BLOCKED: Connect and attach BO2 first.");
				return;
			}
			Log($"{label} START attachedLocalClient={0} chain='{chain}'.");
			int num = Encoding.ASCII.GetByteCount(chain) + 1;
			if (num > 512)
			{
				throw new InvalidOperationException($"{label}: command exceeds the 512-byte limit ({num}).");
			}
			CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, 0, chain);
			Log($"{label} DISPATCH signature={(cbufSendResult.SignatureOk ? "PASS" : "FAIL")} bufferReadback={(cbufSendResult.BufferReadbackOk ? "PASS" : "FAIL")} return=0x{cbufSendResult.ReturnValue:X}.");
			Log(label + " BUFFER '" + cbufSendResult.BufferReadbackAscii + "'.");
			if (cbufSendResult.SignatureOk && cbufSendResult.BufferReadbackOk)
			{
				state.Text = "Dispatched; unverified";
				state.ForeColor = CleanWarning;
				Log(label + " DISPATCHED. Signature and buffer readback passed; game acceptance/effect are unverified.");
			}
			else
			{
				state.Text = "Transport failed";
				state.ForeColor = CleanWarning;
				Log(label + " TRANSPORT FAILED.");
			}
		}
		catch (Exception ex)
		{
			state.Text = "Error";
			state.ForeColor = CleanError;
			Log(label + " FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void ApplyBigNames(bool on, Label state, Label cmd)
	{
		string chain = (cmd.Text = (on ? "cg_overheadNamesSize 2;cg_overheadNamesMaxDist 99999;cg_overheadNamesFarDist 99999;cg_overheadNamesFarScale 1" : "cg_overheadNamesSize 1;cg_overheadNamesMaxDist 1500;cg_overheadNamesFarDist 1000;cg_overheadNamesFarScale 1"));
		SendLocalDvars(chain, state, "BIG-NAMES");
	}

	private void ToggleBigNames(Button btn, Label state, Label cmd)
	{
		object tag = btn.Tag;
		bool flag = !(tag is bool) || !(bool)tag;
		btn.Tag = flag;
		state.Text = (flag ? "ON" : "OFF");
		state.ForeColor = (flag ? CleanSuccess : CleanTextMuted);
		ApplyBigNames(flag, state, cmd);
		state.Text = (flag ? "ON" : "OFF");
		state.ForeColor = (flag ? CleanSuccess : CleanTextMuted);
	}

	private void SendBigNamesInfectionChain(Label state)
	{
		try
		{
			if (debugBridge == null || bo2Process == null)
			{
				Log("BIG-NAMES-INFECTION BLOCKED: Connect and Attach BO2 first.");
				state.Text = "Not connected";
				state.ForeColor = CleanWarning;
				return;
			}
			(string, int) tuple = RequireSelectedWritablePlayer();
			string text = WrapLobbyCommand("selectStringTableEntryInDvar mp/tickerweights.csv 1 sv_cheats;cg_overheadNamesSize 2;cg_overheadNamesMaxDist 99999;cg_overheadNamesFarDist 99999;cg_overheadNamesFarScale 1;g_compassShowEnemies 1;compassShowEnemies 1");
			int num = Encoding.ASCII.GetByteCount(text) + 1;
			if (num > 512)
			{
				throw new InvalidOperationException($"{"BIG-NAMES-INFECTION"}: wrapped command exceeds 512-byte cbuf limit ({num} bytes).");
			}
			Log($"{"BIG-NAMES-INFECTION"} START target='{tuple.Item1}' client={tuple.Item2} len={num - 1}b");
			Log("BIG-NAMES-INFECTION WRAPPED: " + text);
			state.Text = "Sending…";
			state.ForeColor = CleanTextMuted;
			CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, tuple.Item2, text);
			bool num2 = cbufSendResult.SignatureOk && cbufSendResult.BufferReadbackOk;
			Log($"{"BIG-NAMES-INFECTION"} DISPATCH signature={(cbufSendResult.SignatureOk ? "PASS" : "FAIL")} bufferReadback={(cbufSendResult.BufferReadbackOk ? "PASS" : "FAIL")} return=0x{cbufSendResult.ReturnValue:X}");
			Log("BIG-NAMES-INFECTION BUFFER '" + cbufSendResult.BufferReadbackAscii + "'");
			if (num2)
			{
				state.Text = "Dispatched ✓";
				state.ForeColor = CleanSuccess;
				Log("BIG-NAMES-INFECTION DISPATCHED – lobby switch chain sent. Name-size/range dvars and both compass visibility dvars were requested inside the dm→tdm transition. Transport verification confirms the command buffer only, not game-side acceptance or effect.");
			}
			else
			{
				state.Text = "Transport FAIL";
				state.ForeColor = CleanError;
				Log("BIG-NAMES-INFECTION TRANSPORT FAILED – cbuf signature or readback did not verify.");
			}
		}
		catch (Exception ex)
		{
			state.Text = "Error";
			state.ForeColor = CleanError;
			Log("BIG-NAMES-INFECTION FAIL – " + ex.GetBaseException().Message);
		}
	}

	private void SendSuperSpeedChain(Label state)
	{
		try
		{
			if (debugBridge == null || bo2Process == null)
			{
				Log("SUPER-SPEED-INFECTION BLOCKED: Connect and Attach BO2 first.");
				state.Text = "Not connected";
				state.ForeColor = CleanWarning;
				return;
			}
			(string, int) tuple = RequireSelectedWritablePlayer();
			string text = WrapLobbyCommand("selectStringTableEntryInDvar mp/tickerweights.csv 1 sv_cheats;g_speed 450");
			int num = Encoding.ASCII.GetByteCount(text) + 1;
			if (num > 512)
			{
				throw new InvalidOperationException($"{"SUPER-SPEED-INFECTION"}: wrapped command exceeds 512-byte cbuf limit ({num} bytes).");
			}
			Log($"{"SUPER-SPEED-INFECTION"} START target='{tuple.Item1}' client={tuple.Item2} len={num - 1}b");
			Log("SUPER-SPEED-INFECTION WRAPPED: " + text);
			state.Text = "Sending…";
			state.ForeColor = CleanTextMuted;
			CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, tuple.Item2, text);
			bool num2 = cbufSendResult.SignatureOk && cbufSendResult.BufferReadbackOk;
			Log($"{"SUPER-SPEED-INFECTION"} DISPATCH signature={(cbufSendResult.SignatureOk ? "PASS" : "FAIL")} bufferReadback={(cbufSendResult.BufferReadbackOk ? "PASS" : "FAIL")} return=0x{cbufSendResult.ReturnValue:X}");
			Log("SUPER-SPEED-INFECTION BUFFER '" + cbufSendResult.BufferReadbackAscii + "'");
			if (num2)
			{
				state.Text = "Dispatched ✓";
				state.ForeColor = CleanSuccess;
				Log("SUPER-SPEED-INFECTION PASS – lobby switch chain sent. g_speed 450 injected via selectStringTableEntryInDvar inside dm→tdm transition. Wait for lobby switch to complete, then player speed should be increased.");
			}
			else
			{
				state.Text = "Transport FAIL";
				state.ForeColor = CleanError;
				Log("SUPER-SPEED-INFECTION TRANSPORT FAILED – cbuf signature or readback did not verify.");
			}
		}
		catch (Exception ex)
		{
			state.Text = "Error";
			state.ForeColor = CleanError;
			Log("SUPER-SPEED-INFECTION FAIL – " + ex.GetBaseException().Message);
		}
	}

	private void SendForceHostChain(Label state)
	{
		try
		{
			if (debugBridge == null || bo2Process == null)
			{
				Log("FORCE-HOST-INFECTION BLOCKED: Connect and Attach BO2 first.");
				state.Text = "Not connected";
				state.ForeColor = CleanWarning;
				return;
			}
			(string, int) tuple = RequireSelectedWritablePlayer();
			string text = WrapLobbyCommand("selectStringTableEntryInDvar mp/tickerweights.csv 1 sv_cheats;party_connectToOthers 0;partyMigrate_disabled 1;party_mergingEnabled 0;allowAllNAT 0");
			int num = Encoding.ASCII.GetByteCount(text) + 1;
			if (num > 512)
			{
				throw new InvalidOperationException($"{"FORCE-HOST-INFECTION"}: wrapped command exceeds 512-byte cbuf limit ({num} bytes).");
			}
			Log($"{"FORCE-HOST-INFECTION"} START target='{tuple.Item1}' client={tuple.Item2} len={num - 1}b");
			Log("FORCE-HOST-INFECTION WRAPPED: " + text);
			state.Text = "Sending…";
			state.ForeColor = CleanTextMuted;
			CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, tuple.Item2, text);
			bool num2 = cbufSendResult.SignatureOk && cbufSendResult.BufferReadbackOk;
			Log($"{"FORCE-HOST-INFECTION"} DISPATCH signature={(cbufSendResult.SignatureOk ? "PASS" : "FAIL")} bufferReadback={(cbufSendResult.BufferReadbackOk ? "PASS" : "FAIL")} return=0x{cbufSendResult.ReturnValue:X}");
			Log("FORCE-HOST-INFECTION BUFFER '" + cbufSendResult.BufferReadbackAscii + "'");
			if (num2)
			{
				state.Text = "Dispatched ✓";
				state.ForeColor = CleanSuccess;
				Log("FORCE-HOST-INFECTION PASS – lobby switch chain sent. Force Host ON injected via selectStringTableEntryInDvar inside dm→tdm transition. Wait for lobby switch to complete, then you should be forced as host.");
			}
			else
			{
				state.Text = "Transport FAIL";
				state.ForeColor = CleanError;
				Log("FORCE-HOST-INFECTION TRANSPORT FAILED – cbuf signature or readback did not verify.");
			}
		}
		catch (Exception ex)
		{
			state.Text = "Error";
			state.ForeColor = CleanError;
			Log("FORCE-HOST-INFECTION FAIL – " + ex.GetBaseException().Message);
		}
	}

	private void SendFovChain(int fovValue, Label state)
	{
		try
		{
			if (debugBridge == null || bo2Process == null)
			{
				Log("FOV-INFECTION BLOCKED: Connect and Attach BO2 first.");
				state.Text = "Not connected";
				state.ForeColor = CleanWarning;
				return;
			}
			(string, int) tuple = RequireSelectedWritablePlayer();
			string text = WrapLobbyCommand($"cg_fovMin {fovValue}");
			int num = Encoding.ASCII.GetByteCount(text) + 1;
			if (num > 512)
			{
				throw new InvalidOperationException($"{"FOV-INFECTION"}: wrapped command exceeds 512-byte cbuf limit ({num} bytes).");
			}
			Log($"{"FOV-INFECTION"} START target='{tuple.Item1}' client={tuple.Item2} fov={fovValue} len={num - 1}b");
			Log("FOV-INFECTION WRAPPED: " + text);
			state.Text = "Sending…";
			state.ForeColor = CleanTextMuted;
			CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, tuple.Item2, text);
			bool num2 = cbufSendResult.SignatureOk && cbufSendResult.BufferReadbackOk;
			Log($"{"FOV-INFECTION"} DISPATCH signature={(cbufSendResult.SignatureOk ? "PASS" : "FAIL")} bufferReadback={(cbufSendResult.BufferReadbackOk ? "PASS" : "FAIL")} return=0x{cbufSendResult.ReturnValue:X}");
			Log("FOV-INFECTION BUFFER '" + cbufSendResult.BufferReadbackAscii + "'");
			if (num2)
			{
				state.Text = $"FOV {fovValue} ✓";
				state.ForeColor = CleanSuccess;
				Log($"{"FOV-INFECTION"} PASS – lobby switch chain sent. cg_fovMin {fovValue} injected inside dm→tdm transition. " + "Wait for lobby switch to complete, then FOV should be applied.");
			}
			else
			{
				state.Text = "Transport FAIL";
				state.ForeColor = CleanError;
				Log("FOV-INFECTION TRANSPORT FAILED – cbuf signature or readback did not verify.");
			}
		}
		catch (Exception ex)
		{
			state.Text = "Error";
			state.ForeColor = CleanError;
			Log("FOV-INFECTION FAIL – " + ex.GetBaseException().Message);
		}
	}

	private void SendUavChain(Label state)
	{
		try
		{
			if (debugBridge == null || bo2Process == null)
			{
				Log("UAV-CONSTANTE-INFECTION BLOCKED: Connect and Attach BO2 first.");
				state.Text = "Not connected";
				state.ForeColor = CleanWarning;
				return;
			}
			(string, int) tuple = RequireSelectedWritablePlayer();
			string text = WrapLobbyCommand("selectStringTableEntryInDvar mp/tickerweights.csv 1 sv_cheats;g_compassShowEnemies 1;compassShowEnemies 1");
			int num = Encoding.ASCII.GetByteCount(text) + 1;
			if (num > 512)
			{
				throw new InvalidOperationException($"{"UAV-CONSTANTE-INFECTION"}: wrapped command exceeds 512-byte cbuf limit ({num} bytes).");
			}
			Log($"{"UAV-CONSTANTE-INFECTION"} START target='{tuple.Item1}' client={tuple.Item2} len={num - 1}b");
			Log("UAV-CONSTANTE-INFECTION WRAPPED: " + text);
			state.Text = "Sending…";
			state.ForeColor = CleanTextMuted;
			CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, tuple.Item2, text);
			bool num2 = cbufSendResult.SignatureOk && cbufSendResult.BufferReadbackOk;
			Log($"{"UAV-CONSTANTE-INFECTION"} DISPATCH signature={(cbufSendResult.SignatureOk ? "PASS" : "FAIL")} bufferReadback={(cbufSendResult.BufferReadbackOk ? "PASS" : "FAIL")} return=0x{cbufSendResult.ReturnValue:X}");
			Log("UAV-CONSTANTE-INFECTION BUFFER '" + cbufSendResult.BufferReadbackAscii + "'");
			if (num2)
			{
				state.Text = "Dispatched ✓";
				state.ForeColor = CleanSuccess;
				Log("UAV-CONSTANTE-INFECTION DISPATCHED – lobby switch chain sent. g_compassShowEnemies and compassShowEnemies were requested via the dm→tdm transition. Transport verification confirms only the command buffer, not the in-game UAV effect.");
			}
			else
			{
				state.Text = "Transport FAIL";
				state.ForeColor = CleanError;
				Log("UAV-CONSTANTE-INFECTION TRANSPORT FAILED – cbuf signature or readback did not verify.");
			}
		}
		catch (Exception ex)
		{
			state.Text = "Error";
			state.ForeColor = CleanError;
			Log("UAV-CONSTANTE-INFECTION FAIL – " + ex.GetBaseException().Message);
		}
	}

	private async Task SendHeavyAimAssist(Label state)
	{
		try
		{
			if (debugBridge == null || bo2Process == null)
			{
				Log("MAX-AIM-ASSIST-TEST BLOCKED: Connect and attach BO2 first.");
				state.Text = "Not connected";
				state.ForeColor = CleanWarning;
				return;
			}
			(string, string)[] array = new(string, string)[2]
			{
				("lobby switch", "ui_gametype \"dm;selectStringTableEntryInDvar mp/tickerweights.csv 1 sv_cheats;ui_gametype tdm\";xpartyswitchlobbies;ui_gametype tdm"),
				("post-switch aim profile", "selectStringTableEntryInDvar mp/tickerweights.csv 1 sv_cheats;aim_slowdown_enabled 1;aim_slowdown_region_width 640;aim_slowdown_region_height 480;aim_lockon_enabled 1;aim_lockon_debug 1;aim_lockon_strength 1.25;aim_lockon_pitch_strength 1.25;aim_lockon_region_width 640;aim_lockon_region_height 480;aim_lockon_deflection 0.05;aim_autoaim_enabled 1;aim_autoaim_debug 1;aim_autoaim_lerp 180;aim_autoaim_region_width 640;aim_autoaim_region_height 480;aim_autoAimRangeScale 1000;aim_aimAssistRangeScale 1000")
			};
			for (int i = 0; i < array.Length; i++)
			{
				(string, string) tuple = array[i];
				string item = tuple.Item1;
				string item2 = tuple.Item2;
				int num = Encoding.ASCII.GetByteCount(item2) + 1;
				if (num > 512)
				{
					throw new InvalidOperationException($"{"MAX-AIM-ASSIST-TEST"}: {item} exceeds 512-byte cbuf limit ({num} bytes).");
				}
			}
			state.Text = "Switching lobby…";
			state.ForeColor = CleanWarning;
			Log($"{"MAX-AIM-ASSIST-TEST"} PHASE 1/2 switch START attachedLocalClient={0}: {"ui_gametype \"dm;selectStringTableEntryInDvar mp/tickerweights.csv 1 sv_cheats;ui_gametype tdm\";xpartyswitchlobbies;ui_gametype tdm"}");
			CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, 0, "ui_gametype \"dm;selectStringTableEntryInDvar mp/tickerweights.csv 1 sv_cheats;ui_gametype tdm\";xpartyswitchlobbies;ui_gametype tdm");
			bool num2 = cbufSendResult.SignatureOk && cbufSendResult.BufferReadbackOk;
			Log($"{"MAX-AIM-ASSIST-TEST"} PHASE 1/2 signature={(cbufSendResult.SignatureOk ? "PASS" : "FAIL")} bufferReadback={(cbufSendResult.BufferReadbackOk ? "PASS" : "FAIL")} return=0x{cbufSendResult.ReturnValue:X}");
			Log("MAX-AIM-ASSIST-TEST PHASE 1/2 BUFFER '" + cbufSendResult.BufferReadbackAscii + "'");
			if (!num2)
			{
				state.Text = "Switch transport failed";
				state.ForeColor = CleanError;
				Log("MAX-AIM-ASSIST-TEST STOPPED before applying aim DVARs because the lobby-switch transport did not verify.");
				return;
			}
			Log("MAX-AIM-ASSIST-TEST WAIT 5000ms for lobby switch; game-state completion is not observable here.");
			await Task.Delay(5000);
			if (debugBridge == null || bo2Process == null)
			{
				state.Text = "Detached";
				state.ForeColor = CleanWarning;
				Log("MAX-AIM-ASSIST-TEST STOPPED: BO2 attachment was lost during the lobby wait.");
				return;
			}
			state.Text = "Applying profile…";
			state.ForeColor = CleanTextMuted;
			Log($"{"MAX-AIM-ASSIST-TEST"} PHASE 2/2 post-switch START attachedLocalClient={0}: {"selectStringTableEntryInDvar mp/tickerweights.csv 1 sv_cheats;aim_slowdown_enabled 1;aim_slowdown_region_width 640;aim_slowdown_region_height 480;aim_lockon_enabled 1;aim_lockon_debug 1;aim_lockon_strength 1.25;aim_lockon_pitch_strength 1.25;aim_lockon_region_width 640;aim_lockon_region_height 480;aim_lockon_deflection 0.05;aim_autoaim_enabled 1;aim_autoaim_debug 1;aim_autoaim_lerp 180;aim_autoaim_region_width 640;aim_autoaim_region_height 480;aim_autoAimRangeScale 1000;aim_aimAssistRangeScale 1000"}");
			CbufSendResult cbufSendResult2 = debugBridge.SendVerifiedCbufCommand(bo2Process, 0, "selectStringTableEntryInDvar mp/tickerweights.csv 1 sv_cheats;aim_slowdown_enabled 1;aim_slowdown_region_width 640;aim_slowdown_region_height 480;aim_lockon_enabled 1;aim_lockon_debug 1;aim_lockon_strength 1.25;aim_lockon_pitch_strength 1.25;aim_lockon_region_width 640;aim_lockon_region_height 480;aim_lockon_deflection 0.05;aim_autoaim_enabled 1;aim_autoaim_debug 1;aim_autoaim_lerp 180;aim_autoaim_region_width 640;aim_autoaim_region_height 480;aim_autoAimRangeScale 1000;aim_aimAssistRangeScale 1000");
			bool num3 = cbufSendResult2.SignatureOk && cbufSendResult2.BufferReadbackOk;
			Log($"{"MAX-AIM-ASSIST-TEST"} PHASE 2/2 signature={(cbufSendResult2.SignatureOk ? "PASS" : "FAIL")} bufferReadback={(cbufSendResult2.BufferReadbackOk ? "PASS" : "FAIL")} return=0x{cbufSendResult2.ReturnValue:X}");
			Log("MAX-AIM-ASSIST-TEST PHASE 2/2 BUFFER '" + cbufSendResult2.BufferReadbackAscii + "'");
			if (num3)
			{
				state.Text = "Dispatched; unverified";
				state.ForeColor = CleanWarning;
				Log("MAX-AIM-ASSIST-TEST PHASE 2/2 dispatched after the lobby wait. Buffer readback passed; game-side DVAR acceptance/effect and offline mode remain unverified.");
			}
			else
			{
				state.Text = "Aim transport failed";
				state.ForeColor = CleanError;
				Log("MAX-AIM-ASSIST-TEST PHASE 2/2 transport failed; aim DVARs were not verified in the command buffer.");
			}
		}
		catch (Exception ex)
		{
			state.Text = "Error";
			state.ForeColor = CleanError;
			Log("MAX-AIM-ASSIST-TEST FAIL – " + ex.GetBaseException().Message);
		}
	}

	private void SendZeroRecoilTest(Label state)
	{
		SendLocalInfectionProfile(state, "ZERO-RECOIL-TEST", "selectStringTableEntryInDvar mp/tickerweights.csv 1 sv_cheats;adsZeroSpread 1;bg_viewKickScale 0;bg_viewKickMin 0;bg_viewKickMax 0;bg_viewKickRandom 0");
	}

	private void SendFastRadar(Label state)
	{
		SendLocalInfectionProfile(state, "FAST-RADAR", "selectStringTableEntryInDvar mp/tickerweights.csv 1 sv_cheats;compassRadarUpdateTime 0.001");
	}

	private void SendLocalInfectionProfile(Label state, string logLabel, string payload)
	{
		try
		{
			if (debugBridge == null || bo2Process == null)
			{
				Log(logLabel + " BLOCKED: Connect and attach BO2 first.");
				state.Text = "Not connected";
				state.ForeColor = CleanWarning;
				return;
			}
			string text = "ui_gametype \"dm;" + payload + ";ui_gametype tdm\";xpartyswitchlobbies;ui_gametype tdm";
			int num = Encoding.ASCII.GetByteCount(text) + 1;
			if (num > 512)
			{
				throw new InvalidOperationException($"{logLabel}: wrapped command exceeds 512-byte cbuf limit ({num} bytes).");
			}
			Log($"{logLabel} START attachedLocalClient={0} len={num - 1}b");
			Log(logLabel + " WRAPPED: " + text);
			state.Text = "Sending…";
			state.ForeColor = CleanTextMuted;
			CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, 0, text);
			bool num2 = cbufSendResult.SignatureOk && cbufSendResult.BufferReadbackOk;
			Log($"{logLabel} DISPATCH signature={(cbufSendResult.SignatureOk ? "PASS" : "FAIL")} bufferReadback={(cbufSendResult.BufferReadbackOk ? "PASS" : "FAIL")} return=0x{cbufSendResult.ReturnValue:X}");
			Log(logLabel + " BUFFER '" + cbufSendResult.BufferReadbackAscii + "'");
			if (num2)
			{
				state.Text = "Dispatched; unverified";
				state.ForeColor = CleanWarning;
				Log($"{logLabel} DISPATCHED to attached local client {0}. Buffer readback passed; game-side DVAR acceptance/effect and offline mode remain unverified.");
			}
			else
			{
				state.Text = "Transport FAIL";
				state.ForeColor = CleanError;
				Log(logLabel + " TRANSPORT FAILED – cbuf signature or readback did not verify.");
			}
		}
		catch (Exception ex)
		{
			state.Text = "Error";
			state.ForeColor = CleanError;
			Log(logLabel + " FAIL – " + ex.GetBaseException().Message);
		}
	}

	private void SendEspChain(Label state)
	{
		try
		{
			if (debugBridge == null || bo2Process == null)
			{
				Log("ESP-INFECTION BLOCKED: Connect and Attach BO2 first.");
				state.Text = "Not connected";
				state.ForeColor = CleanWarning;
				return;
			}
			(string, int) tuple = RequireSelectedWritablePlayer();
			string text = WrapLobbyCommand("cg_overheadNamesSize 2;cg_overheadNamesMaxDist 99999;cg_overheadNamesFarDist 99999;cg_overheadNamesFarScale 1");
			int num = Encoding.ASCII.GetByteCount(text) + 1;
			if (num > 512)
			{
				throw new InvalidOperationException($"{"ESP-INFECTION"}: wrapped command exceeds 512-byte cbuf limit ({num} bytes).");
			}
			Log($"{"ESP-INFECTION"} START target='{tuple.Item1}' client={tuple.Item2} len={num - 1}b");
			Log("ESP-INFECTION WRAPPED: " + text);
			state.Text = "Sending…";
			state.ForeColor = CleanTextMuted;
			CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, tuple.Item2, text);
			bool num2 = cbufSendResult.SignatureOk && cbufSendResult.BufferReadbackOk;
			Log($"{"ESP-INFECTION"} DISPATCH signature={(cbufSendResult.SignatureOk ? "PASS" : "FAIL")} bufferReadback={(cbufSendResult.BufferReadbackOk ? "PASS" : "FAIL")} return=0x{cbufSendResult.ReturnValue:X}");
			Log("ESP-INFECTION BUFFER '" + cbufSendResult.BufferReadbackAscii + "'");
			if (num2)
			{
				state.Text = "Dispatched ✓";
				state.ForeColor = CleanSuccess;
				Log("ESP-INFECTION DISPATCHED – lobby switch chain sent. Safe fallback sent: Big Names range settings only. Through-wall dvars are disabled because they froze the retail console. Transport verification confirms the command buffer only; it does not confirm game-side dvar application.");
			}
			else
			{
				state.Text = "Transport FAIL";
				state.ForeColor = CleanError;
				Log("ESP-INFECTION TRANSPORT FAILED – cbuf signature or readback did not verify.");
			}
		}
		catch (Exception ex)
		{
			state.Text = "Error";
			state.ForeColor = CleanError;
			Log("ESP-INFECTION FAIL – " + ex.GetBaseException().Message);
		}
	}

	private void ToggleWallhack(Label state)
	{
		try
		{
			if (debugBridge == null || bo2Process == null)
			{
				Log("WALLHACK BLOCKED: Attach BO2 first.");
				return;
			}
			byte b = debugBridge.ReadByte(bo2Process, 4196770uL);
			if (b > 1)
			{
				throw new InvalidOperationException($"Plugin toggle at 0x{4196770uL:X} has unexpected value 0x{b:X2}; left unchanged.");
			}
			byte b2 = ((b == 0) ? ((byte)1) : ((byte)0));
			debugBridge.WriteMemory(bo2Process, 4196770uL, new byte[1] { b2 });
			byte b3 = debugBridge.ReadByte(bo2Process, 4196770uL);
			if (b3 != b2)
			{
				throw new InvalidOperationException($"Toggle verification failed at 0x{4196770uL:X}: expected {b2}, read {b3}.");
			}
			state.Text = ((b2 == 1) ? "ON" : "OFF");
			state.ForeColor = ((b2 == 1) ? CleanSuccess : CleanTextMuted);
			Log($"WALLHACK {((b2 == 1) ? "ON" : "OFF")} verified at plugin variable 0x{4196770uL:X} (0x{b:X2} -> 0x{b3:X2}).");
		}
		catch (Exception ex)
		{
			state.Text = "Error";
			state.ForeColor = CleanError;
			Log("WALLHACK FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void SendCareerStat(string field, decimal value)
	{
		string text = ValidateStatIdentifier(field);
		SendRecoveredProfileCommand("CAREER-" + text, $"statWriteDDL playerstatslist {text} statvalue {(long)value}");
	}

	private void SendStatByName(string statName, decimal value)
	{
		string text = ValidateStatIdentifier(statName);
		SendRecoveredProfileCommand("STATBYNAME-" + text, $"statSetByName {text} {(long)value}");
	}

	private void SendGameTypeStat(string mode, string field, decimal value)
	{
		string text = ValidateStatIdentifier(mode);
		string text2 = ValidateStatIdentifier(field);
		try
		{
			(string, int) tuple = RequireSelectedWritablePlayer();
			string command = WrapLobbyCommand($"statWriteDDL playerstatsbygametype {text} {text2} statvalue {(long)value};updategamerprofile;uploadStats");
			BeginModificationProgress("MODE-" + text + "-" + text2, 1);
			Log($"GAME-MODE V6.17 START target='{tuple.Item1}' mode={text} field={text2} value={(long)value}.");
			CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, tuple.Item2, command);
			if (!cbufSendResult.SignatureOk || !cbufSendResult.BufferReadbackOk)
			{
				throw new InvalidOperationException("Game-mode transport verification failed.");
			}
			MarkModificationApplied("MODE-" + text + "-" + text2);
			Log($"GAME-MODE V6.17 COMMIT PASS return=0x{cbufSendResult.ReturnValue:X}. updategamerprofile + uploadStats were sent atomically with the stat write.");
			ResponsiveValidationDelay(1200);
			SaveSelectedPlayerProfile();
			Log("GAME-MODE V6.17 SAVE PASS. Leave/rejoin and verify persistence.");
		}
		catch (Exception ex)
		{
			Log("GAME-MODE V6.17 FAIL - " + ex.GetBaseException().Message);
		}
	}

	private static string ValidateStatIdentifier(string value)
	{
		string text = (value ?? "").Trim();
		if (text.Length == 0 || text.Any((char ch) => !char.IsLetterOrDigit(ch) && ch != '_'))
		{
			throw new InvalidOperationException("Stat and mode names must use only letters, numbers, and underscores.");
		}
		return text;
	}

	private TabPage ClassesTab()
	{
		TabPage tabPage = new TabPage("Classes");
		FlowLayoutPanel flowLayoutPanel = Stack();
		ComboBox classBox = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 130
		};
		for (int i = 1; i <= 10; i++)
		{
			classBox.Items.Add($"Class {i}");
		}
		classBox.SelectedIndex = 0;
		TextBox className = new TextBox
		{
			PlaceholderText = "Class name",
			Width = 220,
			MaxLength = 15
		};
		ComboBox classColor = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 130
		};
		classColor.Items.AddRange("No Color", "^1 Red", "^2 Green", "^3 Yellow", "^4 Blue", "^5 Cyan", "^6 Pink", "^7 White", "^8 Gray", "^9 Orange", "Manual");
		classColor.SelectedIndex = 0;
		TextBox manualColor = new TextBox
		{
			PlaceholderText = "^ code",
			Width = 65,
			MaxLength = 4,
			Enabled = false
		};
		Label preview = new Label
		{
			Text = "Preview: ",
			AutoSize = true,
			MaximumSize = new Size(700, 0)
		};
		classColor.SelectedIndexChanged += delegate
		{
			RefreshPreview();
		};
		className.TextChanged += delegate
		{
			RefreshPreview();
		};
		manualColor.TextChanged += delegate
		{
			RefreshPreview();
		};
		flowLayoutPanel.Controls.Add(Row(classBox, className, classColor, manualColor, Btn("Preview Selected Player", delegate
		{
			PreviewClassNameTest(classBox.SelectedIndex, FinalClassName());
		}), Btn("Apply Class Name", delegate
		{
			ApplyClassNameTest(classBox.SelectedIndex, FinalClassName());
		})));
		flowLayoutPanel.Controls.Add(Row(preview));
		flowLayoutPanel.Controls.Add(Row(Btn("Apply Name/Color to ALL 10", delegate
		{
			ApplyClassNameAllTen(FinalClassName());
		}), Btn("Random Color - Selected", delegate
		{
			ApplyRandomClassColor(classBox.SelectedIndex, className.Text);
		}), Btn("Random Colors - ALL 10", delegate
		{
			ApplyRandomColorsAllTen(className.Text);
		})));
		flowLayoutPanel.Controls.Add(Row(Btn("Save Selected Player Profile", delegate
		{
			SaveSelectedPlayerProfile();
		})));
		flowLayoutPanel.Controls.Add(Note("V6.12 RETEST: V6.7 class-name persistence failed. This control keeps the recovered setStatFromLocString candidate isolated so it can be tested without affecting proven features."));
		tabPage.Controls.Add(flowLayoutPanel);
		return tabPage;
		string FinalClassName()
		{
			return classColor.SelectedItem?.ToString() switch
			{
				"^1 Red" => "^1", 
				"^2 Green" => "^2", 
				"^3 Yellow" => "^3", 
				"^4 Blue" => "^4", 
				"^5 Cyan" => "^5", 
				"^6 Pink" => "^6", 
				"^7 White" => "^7", 
				"^8 Gray" => "^8", 
				"^9 Orange" => "^9", 
				"Manual" => manualColor.Text.Trim(), 
				_ => "", 
			} + className.Text;
		}
		void RefreshPreview()
		{
			manualColor.Enabled = classColor.SelectedItem?.ToString() == "Manual";
			preview.Text = "Preview: " + FinalClassName();
		}
	}

	private static IEnumerable<string> MedalPair(string field, int value)
	{
		yield return $"statWriteDDL playerstatslist {field} statvalue {value}";
		yield return $"statWriteDDL playerstatslist {field} challengevalue {value}";
	}

	private IEnumerable<string> NormalStatsPresetCommands()
	{
		yield return "statWriteDDL playerstatslist kills statvalue 46585";
		yield return "statWriteDDL playerstatslist wins statvalue 1824";
		yield return "statWriteDDL playerstatslist score statvalue 8098865";
		yield return "statWriteDDL playerstatslist time_played_total statvalue 637819";
		yield return "statWriteDDL playerstatsbygametype dm wins statvalue 1116";
		yield return "statWriteDDL playerstatsbygametype tdm wins statvalue 225";
		yield return "statWriteDDL playerstatsbygametype dom wins statvalue 138";
		yield return "statWriteDDL playerstatsbygametype koth wins statvalue 88";
		yield return "statWriteDDL playerstatsbygametype conf wins statvalue 86";
		yield return "statWriteDDL playerstatsbygametype sd wins statvalue 40";
		yield return "statWriteDDL playerstatsbygametype dem wins statvalue 28";
		yield return "statWriteDDL playerstatsbygametype ctf wins statvalue 22";
		yield return "statWriteDDL playerstatsbygametype hq wins statvalue 20";
		yield return "statWriteDDL playerstatsbygametype gun wins statvalue 15";
		yield return "statWriteDDL playerstatsbygametype oic wins statvalue 12";
		yield return "statWriteDDL playerstatsbygametype shrp wins statvalue 11";
		yield return "statWriteDDL playerstatsbygametype sas wins statvalue 11";
		yield return "statWriteDDL playerstatsbygametype conf wins_hc statvalue 12";
		yield return "statWriteDDL playerstatsbygametype dom wins_hc statvalue 10";
		yield return "statWriteDDL playerstatsbygametype hq wins_hc statvalue 8";
		yield return "statWriteDDL playerstatsbygametype tdm wins_hc statvalue 7";
		yield return "statWriteDDL playerstatsbygametype dem wins_hc statvalue 3";
		yield return "statWriteDDL playerstatsbygametype dm wins_hc statvalue 2";
		yield return "statWriteDDL playerstatsbygametype sd wins_hc statvalue 2";
		yield return "statWriteDDL playerstatsbygametype koth wins_hc statvalue 2";
		yield return "statWriteDDL playerstatsbygametype ctf wins_hc statvalue 0";
		(string, int)[] array = new(string, int)[28]
		{
			("medal_ballistic_knife_kill", 160),
			("medal_kill_enemy_with_their_weapon", 161),
			("medal_backstabber_kill", 180),
			("medal_melee_kill_with_riot_shield", 205),
			("medal_stick_explosive_kill", 245),
			("medal_kill_enemy_injuring_teammate", 526),
			("medal_comeback_from_deathstreak", 570),
			("medal_kill_enemy_when_injured", 630),
			("medal_longshot_kill", 800),
			("medal_stop_enemy_killstreak", 910),
			("medal_kill_enemy_who_killed_teammate", 1061),
			("medal_revenge_kill", 2182),
			("medal_kill_enemy_one_bullet", 3282),
			("medal_headshot", 3930),
			("medal_bounce_hatchet_kill", 10),
			("medal_assisted_suicide", 21),
			("medal_uninterrupted_obit_feed_kills", 40),
			("medal_hatchet_kill", 60),
			("medal_kill_enemy_recent_dive_prone", 87),
			("medal_hacked", 103),
			("medal_first_kill", 105),
			("medal_kill_enemy_after_death", 106),
			("medal_multikill_7", 3),
			("medal_multikill_6", 5),
			("medal_multikill_5", 16),
			("medal_multikill_4", 71),
			("medal_multikill_3", 1346),
			("medal_multikill_2", 1465)
		};
		(string field, int value)[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			(string, int) tuple = array2[i];
			foreach (string item in MedalPair(tuple.Item1, tuple.Item2))
			{
				yield return item;
			}
		}
		(string, int)[] array3 = new(string, int)[26]
		{
			("medal_qrdrone_kill", 30),
			("medal_aitank_kill", 50),
			("medal_microwave_turret_kill", 51),
			("medal_death_machine_kill", 60),
			("medal_multiple_grenade_launcher_kill", 70),
			("medal_helicopter_guard_kill", 90),
			("medal_missile_drone_kill", 96),
			("medal_rcxd_kill", 110),
			("medal_straff_run_kill", 115),
			("medal_sentry_gun_kill", 125),
			("medal_helicopter_gunner_kill", 135),
			("medal_helicopter_comlink_kill", 160),
			("medal_remote_mortar_kill", 170),
			("medal_dogs_kill", 195),
			("medal_plane_mortar_kill", 243),
			("medal_missile_swarm_kill", 250),
			("medal_remote_missile_kill", 312),
			("medal_share_package_helicopter_gunner", 1),
			("medal_share_package_dogs", 1),
			("medal_share_package_emp", 2),
			("medal_share_package_strafe_run", 2),
			("medal_share_package_remote_missile", 2),
			("medal_share_package_satellite", 3),
			("medal_share_package_helicopter_guard", 4),
			("medal_share_package_aitank", 4),
			("medal_share_package_helicopter_comlink", 5)
		};
		array2 = array3;
		for (int i = 0; i < array2.Length; i++)
		{
			(string, int) tuple2 = array2[i];
			foreach (string item2 in MedalPair(tuple2.Item1, tuple2.Item2))
			{
				yield return item2;
			}
		}
	}

	private void ApplyNormalStatsPreset()
	{
		SendRecoveredProfileBatchSafe("NORMAL-STATS+MODES+MEDALS+STREAKS", NormalStatsPresetCommands(), 8, 150, 1200);
	}

	private string RandomClassColorPrefix()
	{
		return ClassColorCodes[classColorRandom.Next(ClassColorCodes.Length)];
	}

	private void ApplyClassNameAllTen(string rawName)
	{
		try
		{
			(string, int) tuple = RequireSelectedWritablePlayer();
			string value = ValidateClassName(rawName);
			Log($"CLASS-ALL START target='{tuple.Item1}' selectedClient={tuple.Item2} value='{value}'.");
			for (int i = 0; i < 10; i++)
			{
				string command = WrapLobbyCommand($"setStatFromLocString cacloadouts customclassname {i} {value}");
				CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, tuple.Item2, command);
				if (!cbufSendResult.SignatureOk || !cbufSendResult.BufferReadbackOk)
				{
					throw new InvalidOperationException($"Class {i + 1} transport verification failed.");
				}
				Log($"CLASS-ALL PASS {i + 1}/10 value='{value}' return=0x{cbufSendResult.ReturnValue:X}.");
				Application.DoEvents();
				ResponsiveValidationDelay(150);
			}
			MarkModificationApplied("CLASS-ALL-10");
			Log("CLASS-ALL COMPLETE. Save Selected Player Profile before leaving the lobby.");
		}
		catch (Exception ex)
		{
			Log("CLASS-ALL FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void ApplyRandomClassColor(int classIndex, string baseName)
	{
		string text = (baseName ?? "").Trim();
		if (text.StartsWith("^") && text.Length >= 2)
		{
			text = text.Substring(2);
		}
		ApplyClassNameTest(classIndex, RandomClassColorPrefix() + text);
	}

	private void ApplyRandomColorsAllTen(string baseName)
	{
		try
		{
			(string, int) tuple = RequireSelectedWritablePlayer();
			string text = (baseName ?? "").Trim();
			if (text.StartsWith("^") && text.Length >= 2)
			{
				text = text.Substring(2);
			}
			if (string.IsNullOrWhiteSpace(text))
			{
				throw new InvalidOperationException("Enter the base class name first.");
			}
			Log($"CLASS-RANDOM-ALL START target='{tuple.Item1}' selectedClient={tuple.Item2} base='{text}'.");
			for (int i = 0; i < 10; i++)
			{
				string value = ValidateClassName(RandomClassColorPrefix() + text);
				string command = WrapLobbyCommand($"setStatFromLocString cacloadouts customclassname {i} {value}");
				CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, tuple.Item2, command);
				if (!cbufSendResult.SignatureOk || !cbufSendResult.BufferReadbackOk)
				{
					throw new InvalidOperationException($"Class {i + 1} transport verification failed.");
				}
				Log($"CLASS-RANDOM-ALL PASS {i + 1}/10 value='{value}' return=0x{cbufSendResult.ReturnValue:X}.");
				Application.DoEvents();
				ResponsiveValidationDelay(150);
			}
			MarkModificationApplied("CLASS-RANDOM-ALL-10");
			Log("CLASS-RANDOM-ALL COMPLETE. Save Selected Player Profile before leaving the lobby.");
		}
		catch (Exception ex)
		{
			Log("CLASS-RANDOM-ALL FAIL - " + ex.GetBaseException().Message);
		}
	}

	private (string Name, int Client) RequireSelectedWritablePlayer()
	{
		if (debugBridge == null || bo2Process == null)
		{
			throw new InvalidOperationException("Attach BO2 first.");
		}
		string text = GetSelectedLivePlayerName();
		if (string.IsNullOrWhiteSpace(text))
		{
			throw new InvalidOperationException("Select an ACTIVE player on the Players tab first.");
		}
		(int, ulong) tuple = debugBridge.ResolveClientNameSlot(bo2Process, text);
		if (tuple.Item1 < 0)
		{
			throw new InvalidOperationException("Selected player '" + text + "' is no longer resolved. Refresh Players.");
		}
		return (Name: text, Client: tuple.Item1);
	}

	private static string ValidateClassName(string value)
	{
		string text = (value ?? "").Trim();
		if (text.Length == 0)
		{
			throw new InvalidOperationException("Enter a class name first.");
		}
		if (text.Length > 15)
		{
			throw new InvalidOperationException("Recovered class-name limit is 15 characters including color codes.");
		}
		if (text.Any((char ch) => ch < ' ' || ch > '~') || text.IndexOfAny(new char[4] { ';', '"', '\r', '\n' }) >= 0)
		{
			throw new InvalidOperationException("Class name contains a character that is unsafe for the BO2 command wrapper. ^ color codes are allowed.");
		}
		return text;
	}

	private static string WrapLobbyCommand(string command)
	{
		return "ui_gametype \"dm;;" + command + ";ui_gametype tdm\";xpartyswitchlobbies;ui_gametype tdm";
	}

	private void PreviewClassNameTest(int classIndex, string rawName)
	{
		try
		{
			(string, int) tuple = RequireSelectedWritablePlayer();
			string value = ValidateClassName(rawName);
			if (classIndex < 0 || classIndex > 9)
			{
				throw new InvalidOperationException("Choose Class 1-10.");
			}
			string text = $"setStatFromLocString cacloadouts customclassname {classIndex} {value}";
			string text2 = WrapLobbyCommand(text);
			Log($"WRITE-PREVIEW target='{tuple.Item1}' selectedClient={tuple.Item2} class={classIndex + 1}");
			Log("WRITE-PREVIEW inner: " + text);
			Log($"WRITE-PREVIEW wrapped bytes={Encoding.ASCII.GetByteCount(text2) + 1}: {text2}");
			Log("WRITE-PREVIEW no command sent.");
		}
		catch (Exception ex)
		{
			Log("WRITE-PREVIEW FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void ApplyClassNameTest(int classIndex, string rawName)
	{
		try
		{
			(string, int) tuple = RequireSelectedWritablePlayer();
			string value = ValidateClassName(rawName);
			if (classIndex < 0 || classIndex > 9)
			{
				throw new InvalidOperationException("Choose Class 1-10.");
			}
			string command = WrapLobbyCommand($"setStatFromLocString cacloadouts customclassname {classIndex} {value}");
			Log($"WRITE-START target='{tuple.Item1}' selectedClient={tuple.Item2} class={classIndex + 1} value='{value}'.");
			CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, tuple.Item2, command);
			Log($"WRITE-CBUF signature={(cbufSendResult.SignatureOk ? "PASS" : "FAIL")} bufferReadback={(cbufSendResult.BufferReadbackOk ? "PASS" : "FAIL")} addr=0x{cbufSendResult.CbufAddress:X} rpcStub=0x{cbufSendResult.RpcStub:X} cmdBuf=0x{cbufSendResult.RemoteCommandBuffer:X} return=0x{cbufSendResult.ReturnValue:X}.");
			Log("WRITE-BUFFER readback='" + cbufSendResult.BufferReadbackAscii + "'.");
			Log($"WRITE-DISPATCH PASS target='{tuple.Item1}' selectedClient={tuple.Item2}. Verify ONLY that player's Class {classIndex + 1} in BO2 before saving.");
		}
		catch (Exception ex)
		{
			Log("WRITE-DISPATCH FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void RunRecoveredClassDiagnostic(bool wrapped)
	{
		try
		{
			(string, int) tuple = RequireSelectedWritablePlayer();
			string text = (wrapped ? WrapLobbyCommand("setStatFromLocString cacloadouts customclassname 0 FORTIS_TEST") : "setStatFromLocString cacloadouts customclassname 0 FORTIS_TEST");
			Log($"DIAG-START mode={(wrapped ? "WRAPPED" : "PLAIN")} target='{tuple.Item1}' selectedClient={tuple.Item2}.");
			Log($"DIAG-COMMAND bytes={Encoding.ASCII.GetByteCount(text) + 1}: {text}");
			CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, tuple.Item2, text);
			Log($"DIAG-CBUF signature={(cbufSendResult.SignatureOk ? "PASS" : "FAIL")} bufferReadback={(cbufSendResult.BufferReadbackOk ? "PASS" : "FAIL")} addr=0x{cbufSendResult.CbufAddress:X} rpcStub=0x{cbufSendResult.RpcStub:X} cmdBuf=0x{cbufSendResult.RemoteCommandBuffer:X} return=0x{cbufSendResult.ReturnValue:X}.");
			Log("DIAG-BUFFER readback='" + cbufSendResult.BufferReadbackAscii + "'.");
			Log("DIAG-DISPATCH completed. Check ONLY selected player's Class 1 for FORTIS_TEST before Save.");
		}
		catch (Exception ex)
		{
			Log("DIAG-FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void SaveSelectedPlayerProfile()
	{
		try
		{
			(string, int) tuple = RequireSelectedWritablePlayer();
			SaveSelectedPlayerProfile(tuple.Item2, tuple.Item1);
		}
		catch (Exception ex)
		{
			Log("SAVE-DISPATCH FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void SaveSelectedPlayerProfile(int selectedClient, string selectedName)
	{
		string command = WrapLobbyCommand("updategamerprofile;uploadStats");
		Log($"SAVE-START target='{selectedName}' selectedClient={selectedClient}.");
		CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, selectedClient, command);
		if (!cbufSendResult.SignatureOk || !cbufSendResult.BufferReadbackOk)
		{
			throw new InvalidOperationException("Selected-player save verification failed.");
		}
		Log($"SAVE-DISPATCH PASS signature=PASS bufferReadback=PASS return=0x{cbufSendResult.ReturnValue:X} target='{selectedName}' selectedClient={selectedClient}.");
		Log("SAVE-BUFFER readback='" + cbufSendResult.BufferReadbackAscii + "'.");
		unsavedModificationCount = 0;
		modificationProgress.Value = 0;
		modificationStatus.Text = $"Classes saved to {selectedName} (Client {selectedClient}) | Unsaved modifications: 0";
	}

	private static string ValidateSimpleValue(string value)
	{
		string text = (value ?? "").Trim();
		if (text.Length == 0)
		{
			throw new InvalidOperationException("Enter a value first.");
		}
		if (text.Any((char ch) => ch < ' ' || ch > '~') || text.IndexOfAny(new char[4] { ';', '"', '\r', '\n' }) >= 0)
		{
			throw new InvalidOperationException("Value contains a character unsafe for the BO2 command wrapper.");
		}
		return text;
	}

	private void BeginModificationProgress(string feature, int total)
	{
		modificationProgress.Value = 0;
		modificationStatus.Text = $"{feature}: 0/{Math.Max(1, total)} | Unsaved modifications: {unsavedModificationCount}";
		Application.DoEvents();
	}

	private void AdvanceModificationProgress(string feature, int done, int total)
	{
		int num = ((total <= 0) ? 100 : ((int)Math.Round((double)done * 100.0 / (double)total)));
		modificationProgress.Value = Math.Max(0, Math.Min(100, num));
		modificationStatus.Text = $"{feature}: {done}/{total} ({num}%) | Unsaved modifications: {unsavedModificationCount}";
		Application.DoEvents();
	}

	private void MarkModificationApplied(string feature)
	{
		unsavedModificationCount++;
		modificationProgress.Value = 100;
		modificationStatus.Text = $"{feature}: applied - SAVE REQUIRED | Unsaved modifications: {unsavedModificationCount}";
	}

	private void SendRecoveredProfileCommand(string feature, string inner)
	{
		try
		{
			(string, int) tuple = RequireSelectedWritablePlayer();
			string text = WrapLobbyCommand(inner);
			int num = Encoding.ASCII.GetByteCount(text) + 1;
			if (num > 512)
			{
				throw new InvalidOperationException($"Recovered command limit exceeded ({num}/512 bytes).");
			}
			BeginModificationProgress(feature, 1);
			Log($"LAN-{feature}-START target='{tuple.Item1}' selectedClient={tuple.Item2}.");
			Log("LAN-" + feature + "-COMMAND " + inner);
			CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, tuple.Item2, text);
			Log($"LAN-{feature}-DISPATCH PASS signature={(cbufSendResult.SignatureOk ? "PASS" : "FAIL")} bufferReadback={(cbufSendResult.BufferReadbackOk ? "PASS" : "FAIL")} bytes={num} return=0x{cbufSendResult.ReturnValue:X}.");
			Log($"LAN-{feature}-BUFFER readback='{cbufSendResult.BufferReadbackAscii}'.");
			MarkModificationApplied(feature);
			Log("LAN-" + feature + "-NEXT Save Selected Player Profile, then P2 leaves/rejoins before verification.");
		}
		catch (Exception ex)
		{
			Log("LAN-" + feature + "-FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void SendRecoveredProfileBatch(string feature, IEnumerable<string> commands)
	{
		try
		{
			(string, int) tuple = RequireSelectedWritablePlayer();
			List<string> list = commands.ToList();
			BeginModificationProgress(feature, list.Count);
			Log($"LAN-{feature}-BATCH-START target='{tuple.Item1}' selectedClient={tuple.Item2} count={list.Count}.");
			int num = 0;
			foreach (string item in list)
			{
				string text = WrapLobbyCommand(item);
				int num2 = Encoding.ASCII.GetByteCount(text) + 1;
				if (num2 > 512)
				{
					throw new InvalidOperationException($"Command {num + 1} exceeds recovered 512-byte limit.");
				}
				CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, tuple.Item2, text);
				if (!cbufSendResult.SignatureOk || !cbufSendResult.BufferReadbackOk)
				{
					throw new InvalidOperationException($"Command {num + 1} transport verification failed.");
				}
				num++;
				Log($"LAN-{feature}-ITEM PASS {num}/{list.Count} bytes={num2} return=0x{cbufSendResult.ReturnValue:X} cmd='{item}'.");
				AdvanceModificationProgress(feature, num, list.Count);
			}
			MarkModificationApplied(feature);
			Log($"LAN-{feature}-BATCH PASS target='{tuple.Item1}' selectedClient={tuple.Item2}. Save, leave/rejoin, verify.");
		}
		catch (Exception ex)
		{
			Log("LAN-" + feature + "-BATCH FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void SendPrestigeMasterRecovery()
	{
		List<string> list = new List<string>
		{
			"statSetByName plevel 11",
			"statSetByName rank 55",
			$"statSetByName rankxp {Bo2LevelXp[55]}",
			"statWriteDDL unlocks 0 245"
		};
		for (int i = 1; i < 32; i++)
		{
			list.Add($"statWriteDDL unlocks {i} 255");
		}
		SendRecoveredProfileBatchSafe("PRESTIGE-MASTER-LEVEL55-UNLOCKS", list, 8, 350, 2000);
	}

	private void SendUnlockRange(int first, int last)
	{
		List<string> list = new List<string>();
		for (int i = first; i <= last; i++)
		{
			int value = ((i == 0) ? 245 : 255);
			list.Add($"statWriteDDL unlocks {i} {value}");
		}
		SendRecoveredProfileBatchSafe($"UNLOCK-RANGE-{first:D2}-{last:D2}", list, 8, 350, 2000);
	}

	private void SendUnlockBitfield()
	{
		List<string> list = new List<string> { "statWriteDDL unlocks 0 245" };
		for (int i = 1; i < 32; i++)
		{
			list.Add($"statWriteDDL unlocks {i} 255");
		}
		SendRecoveredProfileBatchSafe("FULL-UNLOCK-RECOVERY", list, 8, 350, 2000);
	}

	private TabPage AdvancedClassesTab()
	{
		TabPage tabPage = new TabPage("Advanced Classes");
		FlowLayoutPanel flowLayoutPanel = Stack();
		flowLayoutPanel.Controls.Add(Note("Standalone Advanced Classes. This feature is NOT part of Full Recovery. Uses the recovered class-editor field tables and the selected LAN player."));
		ComboBox cls = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 110
		};
		for (int i = 1; i <= 10; i++)
		{
			cls.Items.Add($"Class {i}");
		}
		cls.SelectedIndex = 0;
		ComboBox primary = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 145
		};
		primary.Items.AddRange(AdvancedWeaponIds.Keys.Cast<object>().ToArray());
		primary.SelectedItem = "Ballista";
		ComboBox secondary = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 145
		};
		secondary.Items.AddRange(AdvancedWeaponIds.Keys.Cast<object>().ToArray());
		secondary.SelectedItem = "Executioner";
		ComboBox pa1 = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 130
		};
		pa1.Items.AddRange(AdvancedAttachmentIds.Keys.Cast<object>().ToArray());
		pa1.SelectedItem = "Ballistics CPU";
		ComboBox sa1 = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 130
		};
		sa1.Items.AddRange(AdvancedAttachmentIds.Keys.Cast<object>().ToArray());
		sa1.SelectedItem = "Fast Mag";
		ComboBox wc = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 135
		};
		wc.Items.AddRange(AdvancedWildcardIds.Keys.Cast<object>().ToArray());
		wc.SelectedItem = "Overkill";
		flowLayoutPanel.Controls.Add(Row(cls, primary, secondary, pa1, sa1, wc, Btn("Apply Custom Builder", delegate
		{
			ApplyAdvancedClass(cls.SelectedIndex, primary.Text, secondary.Text, pa1.Text, sa1.Text, wc.Text);
		})));
		flowLayoutPanel.Controls.Add(Note("Recovered editor supports primary/secondary, three attachments each, camos, perk groups, lethal/tactical equipment and all three wildcard fields. V6.20 exposes the core recovered double/Overkill path first; presets use the same transaction writer."));
		flowLayoutPanel.Controls.Add(Row(Btn("Double Riot Shield", delegate
		{
			ApplyAdvancedClass(cls.SelectedIndex, "Riot Shield", "Riot Shield", "Suppressor", "Fast Mag", "Overkill");
		}), Btn("Double Ballista", delegate
		{
			ApplyAdvancedClass(cls.SelectedIndex, "Ballista", "Ballista", "Ballistics CPU", "Fast Mag", "Overkill");
		}), Btn("Ballista + DSR", delegate
		{
			ApplyAdvancedClass(cls.SelectedIndex, "Ballista", "DSR-50", "Ballistics CPU", "Fast Mag", "Overkill");
		}), Btn("DSR + Ballista", delegate
		{
			ApplyAdvancedClass(cls.SelectedIndex, "DSR-50", "Ballista", "Fast Mag", "Ballistics CPU", "Overkill");
		})));
		flowLayoutPanel.Controls.Add(Row(Btn("Ballista + Executioner", delegate
		{
			ApplyAdvancedClass(cls.SelectedIndex, "Ballista", "Executioner", "Ballistics CPU", "Fast Mag", "Overkill");
		}), Btn("Crossbow + Ballistic Knife", delegate
		{
			ApplyAdvancedClass(cls.SelectedIndex, "Crossbow", "Ballistic Knife", "ACOG", "Fast Mag", "Overkill");
		})));
		flowLayoutPanel.Controls.Add(Row(Btn("Prepare Online / LAN", delegate
		{
			PrepareLanOnline();
		}), Btn("Save Selected Player Classes", delegate
		{
			SaveSelectedPlayerProfile();
		}), Btn("Save + End Game", delegate
		{
			SaveAndEndGame();
		})));
		flowLayoutPanel.Controls.Add(Note("Advanced Classes remain in PREPARE LAN. Applying or saving a class NEVER switches to Public Match. Use PUBLIC MATCH only from Lobby controls when you intentionally start a match."));
		tabPage.Controls.Add(flowLayoutPanel);
		return tabPage;
	}

	private void ApplyAdvancedClass(int classIndex, string primary, string secondary, string primaryAttachment, string secondaryAttachment, string wildcard)
	{
		try
		{
			(string, int) tuple = RequireSelectedWritablePlayer();
			SessionModeResult sessionModeResult = debugBridge.PrepareLanProfile(bo2Process);
			Log($"ADV-CLASS PREPARE-LAN PASS pid={sessionModeResult.Pid} rpc=0x{sessionModeResult.RpcStub:X} SessionMode_SetMode=0x{sessionModeResult.SetModeAddress:X}. Session mode intentionally unchanged.");
			if (classIndex < 0 || classIndex > 9)
			{
				throw new InvalidOperationException("Choose Class 1-10.");
			}
			int value = AdvancedWeaponIds[primary];
			int value2 = AdvancedWeaponIds[secondary];
			int value3 = AdvancedAttachmentIds[primaryAttachment];
			int value4 = AdvancedAttachmentIds[secondaryAttachment];
			int value5 = AdvancedWildcardIds[wildcard];
			string[] array = new string[5]
			{
				$"statWriteDDL CACLoadouts customclass {classIndex} Primary {value}",
				$"statWriteDDL CACLoadouts customclass {classIndex} Secondary {value2}",
				$"statWriteDDL CACLoadouts customclass {classIndex} Primaryattachment1 {value3}",
				$"statWriteDDL CACLoadouts customclass {classIndex} Secondaryattachment1 {value4}",
				$"statWriteDDL CACLoadouts customclass {classIndex} BonusCard1 {value5}"
			};
			Log($"ADV-CLASS START target='{tuple.Item1}' selectedClient={tuple.Item2} class={classIndex + 1} primary={primary}/{value} secondary={secondary}/{value2} wildcard={wildcard}/{value5}.");
			string text = WrapLobbyCommand(string.Join(";", array));
			Log($"ADV-CLASS WRITE target='{tuple.Item1}' selectedClient={tuple.Item2} fields={array.Length} bytes={Encoding.ASCII.GetByteCount(text) + 1}.");
			CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, tuple.Item2, text);
			if (!cbufSendResult.SignatureOk || !cbufSendResult.BufferReadbackOk)
			{
				throw new InvalidOperationException("Advanced class write failed transport verification.");
			}
			Log($"ADV-CLASS WRITE PASS selectedClient={tuple.Item2} signature=PASS bufferReadback=PASS return=0x{cbufSendResult.ReturnValue:X}.");
			ResponsiveValidationDelay(500);
			MarkModificationApplied("ADVANCED-CLASSES");
			SaveSelectedPlayerProfile(tuple.Item2, tuple.Item1);
			Log($"ADV-CLASS COMPLETE. Write and save both remained pinned to client {tuple.Item2} ('{tuple.Item1}'). Leave/rejoin before judging persistence.");
		}
		catch (Exception ex)
		{
			Log("ADV-CLASS FAIL - " + ex.GetBaseException().Message);
		}
	}

	private TabPage TrophiesTab()
	{
		TabPage tabPage = new TabPage("Trophies");
		FlowLayoutPanel flowLayoutPanel = Stack();
		flowLayoutPanel.Controls.Add(SectionTitle("TROPHIES", "WORKING…"));
		flowLayoutPanel.Controls.Add(new Label
		{
			Text = "Updating this tool. Trophy and GSC options are temporarily disabled.",
			AutoSize = true,
			ForeColor = CleanTextMuted,
			Font = new Font("Segoe UI", 10f),
			Margin = new Padding(12, 10, 12, 16)
		});
		tabPage.Controls.Add(flowLayoutPanel);
		return tabPage;
	}

	private void InjectRoyalTrophyMenu()
	{
		if (StandaloneGscOptionsUpdating)
		{
			gscStatus.Text = "UPDATING";
			Log("TROPHY-GSC UPDATING: GSC loading is temporarily disabled.");
			return;
		}
		if (debugBridge == null || bo2Process == null)
		{
			Log("TROPHY-GSC blocked: Connect and Attach BO2 first.");
			return;
		}
		try
		{
			(string, int) tuple = RequireSelectedWritablePlayer();
			if (MessageBox.Show($"Load the proven Royal trophy GSC for retail-player testing?\n\nSelected player: {tuple.Item1}\nClient index: {tuple.Item2}\n\nThis intentionally does NOT call the jailbroken host's local trophy API.", "Retail Trophy Test", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes)
			{
				Log($"TROPHY-RETAIL TEST START selected='{tuple.Item1}' selectedClient={tuple.Item2} target=maps/mp/gametypes/_clientids.gsc.");
				byte[] source = ReadEmbeddedAsset("r_1C7D6B30");
				GscInjectResult gscInjectResult = debugBridge.InjectCompiledGsc(bo2Process, source, "royal_menu_ps4.gscc");
				gscStatus.Text = "Retail trophy GSC loaded";
				Log($"TROPHY-RETAIL GSC INJECT PASS size={gscInjectResult.Size} asset=0x{gscInjectResult.AssetHeader:X} buffer=0x{gscInjectResult.InjectedBuffer:X} checksum=0x{gscInjectResult.StockChecksum:X8}.");
				Log("TROPHY-RETAIL READY. Start the LAN match; at countdown 3 press Public Match. Do not fast_restart this path.");
				Log("TROPHY-RETAIL NOTE: Royal contains the proven giveachievement routine. The selected-player bridge trigger remains available for bridge validation.");
			}
		}
		catch (Exception ex)
		{
			gscStatus.Text = "Inject failed";
			Log("TROPHY-RETAIL ERROR: " + ex.GetBaseException().Message);
		}
	}

	private void TriggerSelectedPlayerTrophies()
	{
		if (StandaloneGscOptionsUpdating)
		{
			Log("TROPHY-GSC UPDATING: the trophy bridge is temporarily disabled.");
			return;
		}
		try
		{
			(string, int) tuple = RequireSelectedWritablePlayer();
			trophyTriggerCounter++;
			string[] array = new string[2]
			{
				$"set blaide_trophy_client {tuple.Item2}",
				$"set blaide_trophy_fire {trophyTriggerCounter}"
			};
			foreach (string command in array)
			{
				CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, tuple.Item2, command);
				if (!cbufSendResult.SignatureOk || !cbufSendResult.BufferReadbackOk)
				{
					throw new InvalidOperationException("Trophy bridge Cbuf verification failed.");
				}
			}
			Log($"TROPHY-UNLOCK-ALL REQUEST PASS selected='{tuple.Item1}' selectedClient={tuple.Item2} request={trophyTriggerCounter}. This confirms RTM dispatch only; the in-game Royal counter is authoritative for actual achievement execution.");
		}
		catch (Exception ex)
		{
			Log("TROPHY-BRIDGE FAIL - " + ex.GetBaseException().Message);
		}
	}

	private TabPage LobbyTab()
	{
		TabPage tabPage = new TabPage("Lobby / Permissions");
		FlowLayoutPanel flowLayoutPanel = Stack();
		flowLayoutPanel.Controls.Add(Note("Recovered Fortis BO2 workflow: Prepare LAN in the lobby. Start the match. At countdown 3 press Public Match (Online ON, Private OFF, System Link OFF). Use End Game when finished."));
		flowLayoutPanel.Controls.Add(Row(Btn("spooffer", delegate
		{
			ApplyLanPlayModes();
		}), Btn("PUBLIC MATCH", delegate
		{
			ApplyPublicSession("PUBLIC-MATCH");
		}), Btn("END GAME", delegate
		{
			EndGame();
		})));
		flowLayoutPanel.Controls.Add(Row(Btn("Save Selected Player", delegate
		{
			SaveSelectedPlayerProfile();
		}), Btn("Save + End Game", delegate
		{
			SaveAndEndGame();
		})));
		flowLayoutPanel.Controls.Add(Note(zombiesMode ? "Zombies: load the ZM menu before starting the match, then Public Match at countdown 3. ZM asset replacement remains crash-protected until its separate lookup path is corrected." : "Multiplayer: this session path is used by Advanced Classes and the Royal trophy/menu workflow."));
		tabPage.Controls.Add(flowLayoutPanel);
		return tabPage;
	}

	private void PrepareLanOnline()
	{
		try
		{
			if (debugBridge == null || bo2Process == null)
			{
				throw new InvalidOperationException("Connect and Attach BO2 first.");
			}
			SessionModeResult sessionModeResult = debugBridge.PrepareLanProfile(bo2Process);
			Log($"PREPARE-LAN PASS pid={sessionModeResult.Pid} rpc=0x{sessionModeResult.RpcStub:X} SessionMode_SetMode=0x{sessionModeResult.SetModeAddress:X}. No session mode changed yet.");
		}
		catch (Exception ex)
		{
			Log("PREPARE-LAN FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void ApplyLanPlayModes()
	{
		try
		{
			if (debugBridge == null || bo2Process == null)
			{
				throw new InvalidOperationException("Connect and Attach BO2 first.");
			}
			SessionModeResult sessionModeResult = debugBridge.SetLanPlayModes(bo2Process);
			Log($"LAN-PLAY MODES PASS pid={sessionModeResult.Pid}: SystemLink=OFF, Online=ON; GameMode(1)=OFF, GameMode(5)=OFF, GameMode(0)=ON.");
		}
		catch (Exception ex)
		{
			Log("LAN-PLAY MODES FAIL - " + ex.GetBaseException().Message);
		}
	}

	private bool ApplyPublicSession(string tag)
	{
		try
		{
			if (debugBridge == null || bo2Process == null)
			{
				throw new InvalidOperationException("Connect and Attach BO2 first.");
			}
			SessionModeResult sessionModeResult = debugBridge.SetPublicMatchSession(bo2Process);
			Log($"{tag} PASS pid={sessionModeResult.Pid}: Online=ON, Private=OFF, SystemLink=OFF via SessionMode_SetMode.");
			return true;
		}
		catch (Exception ex)
		{
			Log(tag + " FAIL - " + ex.GetBaseException().Message);
			return false;
		}
	}

	private void EndGame()
	{
		try
		{
			if (debugBridge == null || bo2Process == null)
			{
				throw new InvalidOperationException("Connect and Attach BO2 first.");
			}
			CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, 0, "killserver;xstopprivateparty;xstopparty");
			if (!cbufSendResult.SignatureOk || !cbufSendResult.BufferReadbackOk)
			{
				throw new InvalidOperationException("End Game transport verification failed.");
			}
			Log($"END-GAME PASS return=0x{cbufSendResult.ReturnValue:X}: killserver, xstopprivateparty, xstopparty.");
		}
		catch (Exception ex)
		{
			Log("END-GAME FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void SaveAndEndGame()
	{
		try
		{
			(string, int) tuple = RequireSelectedWritablePlayer();
			SaveSelectedPlayerProfile(tuple.Item2, tuple.Item1);
			ResponsiveValidationDelay(350);
			EndGame();
			Log($"SAVE+END COMPLETE target='{tuple.Item1}' client={tuple.Item2}.");
		}
		catch (Exception ex)
		{
			Log("SAVE+END FAIL - " + ex.GetBaseException().Message);
		}
	}

	private TabPage DiagnosticsTab()
	{
		return new TabPage("Diagnostics")
		{
			Controls = { (Control?)log }
		};
	}

	private async Task ConnectAsync()
	{
		if (!IPAddress.TryParse(ipBox.Text.Trim(), out IPAddress _))
		{
			Log("Invalid PS4 IP address.");
			return;
		}
		Disconnect(writeLog: false);
		ps4Status.Text = "Probing...";
		try
		{
			debugProbe = new TcpClient();
			using CancellationTokenSource cts = new CancellationTokenSource(2500);
			await debugProbe.ConnectAsync(ipBox.Text.Trim(), 744, cts.Token);
			ps4Status.Text = "Connected / port reachable";
			Log("PS4 debug service port 744 is reachable.");
			monitor.Start();
			try
			{
				string notifyIp = ipBox.Text.Trim();
				await Task.Run(delegate
				{
					DebugBridge debugBridge = new DebugBridge(notifyIp);
					try
					{
						debugBridge.Connect();
						debugBridge.NotifyConsole(222, "@wyzyxc BO2 RTM connected");
					}
					finally
					{
						try
						{
							debugBridge.Disconnect();
						}
						catch
						{
						}
					}
				});
				Log("PS4 system notification acknowledged by the debug payload.");
			}
			catch (Exception ex)
			{
				Log("PS4 system notification failed: " + ex.GetBaseException().Message);
			}
			Notify.Alert("Tool connected", "@wyzyxc tool connected done");
		}
		catch (Exception ex2)
		{
			ps4Status.Text = "Not connected";
			Log("Connection probe failed: " + ex2.Message);
			debugProbe?.Dispose();
			debugProbe = null;
		}
	}

	private void Disconnect(bool writeLog = true)
	{
		monitor.Stop();
		debugProbe?.Dispose();
		debugProbe = null;
		debugBridge = null;
		bo2Process = null;
		ps4Status.Text = "Disconnected";
		bo2Status.Text = "Not attached";
		gameState.Text = "Not checked";
		clientsStatus.Text = "0";
		if (writeLog)
		{
			Log("Disconnected.");
		}
	}

	private async Task AttachBO2Async()
	{
		if (debugProbe == null)
		{
			Log("Connect to the PS4 first.");
			return;
		}
		string ip = ipBox.Text.Trim();
		DebugBridge pendingBridge = null;
		if (attachButton != null)
		{
			attachButton.Enabled = false;
			attachButton.Text = "Attaching...";
		}
		bo2Status.Text = "Searching...";
		gameState.Text = "Attaching...";
		try
		{
			AttachSnapshot attachSnapshot = await Task.Run(delegate
			{
				DebugBridge debugBridge = (pendingBridge = new DebugBridge(ip));
				debugBridge.Connect();
				ProcessSearchResult processSearchResult = debugBridge.FindBO2Process(zombiesMode);
				List<string> list = new List<string>(processSearchResult.Diagnostics);
				if (processSearchResult.Process != null)
				{
					try
					{
						MemoryReadResult memoryReadResult = debugBridge.ValidateRead(processSearchResult.Process);
						list.Add("Memory read test: PASS");
						list.Add($"PID: {memoryReadResult.Pid}");
						list.Add($"Address: 0x{memoryReadResult.Address:X}");
						list.Add($"Requested: {memoryReadResult.Requested} bytes");
						list.Add($"Returned: {memoryReadResult.Returned} bytes");
						list.Add("Preview: " + memoryReadResult.Preview);
						list.Add("No game memory was modified.");
					}
					catch (Exception ex2)
					{
						list.Add("Memory read test: FAIL - " + ex2.GetBaseException().Message);
						list.Add("No game memory was modified.");
					}
					try
					{
						BO2ValidationResult bO2ValidationResult = debugBridge.ValidateBO2Specific(processSearchResult.Process);
						list.Add("BO2-specific validation: PASS");
						list.Add($"Known DB function: 0x{bO2ValidationResult.DbAddress:X}");
						list.Add($"Resolved BO2 game base: 0x{bO2ValidationResult.GameBase:X}");
						list.Add($"Client-base pointer address: 0x{bO2ValidationResult.ClientBasePtrAddress:X}");
						list.Add($"Client-base pointer value: 0x{bO2ValidationResult.ClientBasePtrValue:X}");
						list.Add("BO2-specific validation was read-only; no game memory was modified.");
					}
					catch (Exception ex3)
					{
						list.Add("BO2-specific validation: FAIL - " + ex3.GetBaseException().Message);
						list.Add("No game memory was modified.");
					}
				}
				return new AttachSnapshot(debugBridge, processSearchResult.Process, processSearchResult.DisplayName, list);
			});
			debugBridge = attachSnapshot.Bridge;
			bo2Process = attachSnapshot.Process;
			foreach (string logLine in attachSnapshot.LogLines)
			{
				Log(logLine);
			}
			if (bo2Process == null)
			{
				bo2Status.Text = "Not found";
				gameState.Text = "BO2 not detected";
				Log("BO2 process was not identified. Leave BO2 running and retry Attach.");
				return;
			}
			bo2Status.Text = attachSnapshot.DisplayName;
			gameState.Text = "BO2 ready";
			Log("BO2 process selected: " + attachSnapshot.DisplayName);
			Log("V3.3 process-selection test passed.");
			Notify.Alert("Tool connected", "@wyzyxc tool connected done");
			Log("Automatically refreshing the live lobby player list after BO2 attach.");
			await RefreshPlayersAsync();
		}
		catch (Exception ex)
		{
			bo2Status.Text = "Attach failed";
			gameState.Text = "Check failed";
			Log("Attach error: " + ex.GetBaseException().Message);
			if (pendingBridge == null)
			{
				return;
			}
			foreach (string item in pendingBridge.DescribeApi())
			{
				Log(item);
			}
		}
		finally
		{
			if (attachButton != null)
			{
				attachButton.Enabled = true;
				attachButton.Text = "Attach BO2";
			}
		}
	}

	private void MonitorTick()
	{
		if (debugProbe != null)
		{
			LogQuiet((bo2Process == null) ? "Monitor: PS4 reachable; BO2 not attached." : "Monitor: PS4 reachable; BO2 process selected (read-only V3.6.1 test).");
		}
	}

	private static List<string> NameCandidates(byte[] record)
	{
		List<string> list = new List<string>();
		int i = 0;
		while (i < record.Length)
		{
			if (record[i] < 33 || record[i] > 126)
			{
				i++;
				continue;
			}
			int num = i;
			for (; i < record.Length && record[i] >= 33 && record[i] <= 126; i++)
			{
			}
			int num2 = i - num;
			if (num2 >= 3 && num2 <= 24 && (i == record.Length || record[i] == 0))
			{
				string text = Encoding.ASCII.GetString(record, num, num2);
				if (text.Any(char.IsLetterOrDigit) && text.All((char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.'))
				{
					list.Add(text);
				}
			}
		}
		return list.Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
	}

	private void StartLanRecording()
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("Attach BO2 first.");
			return;
		}
		if (lanCaptureTimer.Enabled || lanCaptureBusy)
		{
			Log("LAN recording is already running.");
			return;
		}
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "BO2_LAN_Diagnostics", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
		try
		{
			Directory.CreateDirectory(text);
			lanCaptureFolder = text;
			lanCaptureIndex = 0;
			lanPreviousRegions = null;
			lanPreviousRecords = null;
			lanCaptureStarted = DateTime.Now;
			File.WriteAllText(Path.Combine(text, "events.csv"), "timestamp_local,elapsed_ms,event,detail" + Environment.NewLine);
			MarkLanEvent("RECORDING_STARTED");
			lanCaptureTimer.Start();
			Log("LAN-REC started; folder=" + text + "; 1-second scheduled samples. Mark leave/rejoin immediately with buttons.");
			CaptureLanSampleAsync();
		}
		catch (Exception ex)
		{
			Log("LAN-REC start failed: " + ex.Message);
			lanCaptureFolder = null;
		}
	}

	private void MarkLanEvent(string label)
	{
		if (lanCaptureFolder == null)
		{
			Log("Start LAN Recording first.");
			return;
		}
		try
		{
			string value = DateTime.Now.ToString("O");
			long value2 = (long)(DateTime.Now - lanCaptureStarted).TotalMilliseconds;
			File.AppendAllText(Path.Combine(lanCaptureFolder, "events.csv"), $"{value},{value2},{label},user_marker" + Environment.NewLine);
			Log($"LAN-MARK {label} +{value2}ms");
			if (label != "RECORDING_STARTED" && label != "RECORDING_STOPPED")
			{
				CaptureLanSampleAsync();
			}
		}
		catch (Exception ex)
		{
			Log("LAN-MARK failed: " + ex.Message);
		}
	}

	private async Task CaptureLanSampleAsync()
	{
		if (lanCaptureBusy || lanCaptureFolder == null || debugBridge == null || bo2Process == null)
		{
			return;
		}
		lanCaptureBusy = true;
		string folder = lanCaptureFolder;
		int index = ++lanCaptureIndex;
		DebugBridge bridge = debugBridge;
		object process = bo2Process;
		try
		{
			DateTime now = DateTime.Now;
			long startElapsed = (long)(now - lanCaptureStarted).TotalMilliseconds;
			(byte[], List<DebugBridge.RegionSnapshot>) tuple = await Task.Run(() => (Records: bridge.ReadAllPlayerRecords80(process), Regions: bridge.ReadCandidateRegions(process)));
			string text = $"sample_{index:D4}";
			File.WriteAllBytes(Path.Combine(folder, text + "_player_records.bin"), tuple.Item1);
			using (BinaryWriter binaryWriter = new BinaryWriter(File.Create(Path.Combine(folder, text + "_regions.bin"))))
			{
				binaryWriter.Write(tuple.Item2.Count);
				foreach (DebugBridge.RegionSnapshot item in tuple.Item2)
				{
					binaryWriter.Write(item.Address);
					binaryWriter.Write(item.Bytes.Length);
					binaryWriter.Write(item.Bytes);
				}
			}
			int value = ((lanPreviousRecords == null) ? (-1) : tuple.Item1.Zip(lanPreviousRecords, (byte a, byte b) => (a != b) ? 1 : 0).Sum());
			int num = -1;
			if (lanPreviousRegions != null)
			{
				Dictionary<ulong, DebugBridge.RegionSnapshot> dictionary = lanPreviousRegions.ToDictionary((DebugBridge.RegionSnapshot r) => r.Address);
				num = 0;
				foreach (DebugBridge.RegionSnapshot item2 in tuple.Item2)
				{
					if (!dictionary.TryGetValue(item2.Address, out var value2))
					{
						continue;
					}
					int num2 = Math.Min(item2.Bytes.Length, value2.Bytes.Length);
					for (int num3 = 0; num3 < num2; num3++)
					{
						if (item2.Bytes[num3] != value2.Bytes[num3])
						{
							num++;
						}
					}
					num += Math.Abs(item2.Bytes.Length - value2.Bytes.Length);
				}
			}
			lanPreviousRecords = tuple.Item1;
			lanPreviousRegions = tuple.Item2;
			long value3 = (long)(DateTime.Now - lanCaptureStarted).TotalMilliseconds;
			File.AppendAllText(Path.Combine(folder, "events.csv"), $"{DateTime.Now:O},{value3},SAMPLE,{text};read_start_ms={startElapsed};read_end_ms={value3};record_changed_bytes={value};region_changed_bytes={num}" + Environment.NewLine);
			Log($"LAN-SAMPLE {index:D4} recordChanged={value} regionChanged={num} (unverified; read-only)");
		}
		catch (Exception ex)
		{
			Log($"LAN-SAMPLE {index:D4} failed: {ex.GetBaseException().Message}");
			try
			{
				File.AppendAllText(Path.Combine(folder, "events.csv"), $"{DateTime.Now:O},0,ERROR,sample_{index:D4}" + Environment.NewLine);
			}
			catch
			{
			}
		}
		finally
		{
			lanCaptureBusy = false;
		}
	}

	private void StopLanRecording()
	{
		lanCaptureTimer.Stop();
		if (lanCaptureFolder == null)
		{
			Log("LAN recording is not running.");
			return;
		}
		MarkLanEvent("RECORDING_STOPPED");
		string text = lanCaptureFolder;
		lanCaptureFolder = null;
		Log($"LAN-REC stopped. Samples={lanCaptureIndex}; folder={text}. If a sample is in progress, wait for its completion before zipping.");
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = text,
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			Log("Open folder failed: " + ex.Message);
		}
	}

	private void CapturePresenceFocus()
	{
		try
		{
			byte[] array = debugBridge.ReadPresenceFocusWindow(bo2Process, 63858577uL, 1024, 1024);
			ulong num = 63857553uL;
			if (presenceFocusBaseline == null)
			{
				presenceFocusBaseline = array;
				Log($"LIVE-FOCUS BASELINE captured addr=0x{63858577uL:X} range=0x{num:X}-0x{(ulong)((long)num + (long)array.Length - 1):X} bytes={array.Length}. Current state should be P2 OUT.");
				Log("LIVE-FOCUS next: have P2 JOIN, then Refresh Players once.");
				return;
			}
			List<string> list = new List<string>();
			int num2 = Math.Min(presenceFocusBaseline.Length, array.Length);
			for (int i = 0; i < num2; i++)
			{
				if (presenceFocusBaseline[i] != array[i])
				{
					list.Add($"0x{(ulong)((long)num + (long)i):X}(rel {i - 1024:+0;-0;0}):{presenceFocusBaseline[i]:X2}>{array[i]:X2}");
				}
			}
			Log($"LIVE-FOCUS DELTA changed={list.Count}/{num2} around 0x{63858577uL:X}.");
			foreach (string[] item in list.Chunk(24))
			{
				Log("LIVE-FOCUS CHANGES " + string.Join(" ", item));
			}
			if (list.Count == 0)
			{
				Log("LIVE-FOCUS NO_CHANGE in +/-0x400 window.");
			}
			else
			{
				Log("LIVE-FOCUS compare this JOIN/LEAVE delta against the next refresh; baseline remains the original P2-OUT capture.");
			}
		}
		catch (Exception ex)
		{
			Log("LIVE-FOCUS failed: " + ex.GetBaseException().Message);
		}
	}

	private static string ReadLiveFocusName(DebugBridge bridge, object process)
	{
		byte[] array = bridge.ReadPresenceFocusWindow(process, 63858577uL, 0, 31);
		List<byte> list = new List<byte>();
		byte[] array2 = array;
		foreach (byte b in array2)
		{
			if (b < 32 || b > 126)
			{
				break;
			}
			list.Add(b);
		}
		return Encoding.ASCII.GetString(list.ToArray());
	}

	private async void RefreshPlayersStub()
	{
		await RefreshPlayersAsync();
	}

	private async Task RefreshPlayersAsync()
	{
		DebugBridge bridge = debugBridge;
		object process = bo2Process;
		if (bridge == null || process == null)
		{
			Log("Attach BO2 first.");
		}
		else
		{
			if (playersRefreshInProgress)
			{
				return;
			}
			playersRefreshInProgress = true;
			try
			{
				Log("Players refresh started (V1 resolver integration; read-only).");
				string selectedName = selectedLivePlayerName;
				List<string> source = await Task.Run(() => bridge.ResolveVerifiedLivePlayerNames(process));
				if (debugBridge != bridge || bo2Process != process)
				{
					return;
				}
				source = (from n in source.Distinct<string>(StringComparer.OrdinalIgnoreCase)
					orderby (!string.Equals(n, selectedName, StringComparison.OrdinalIgnoreCase)) ? 1 : 0
					select n).ThenBy<string, string>((string n) => n, StringComparer.OrdinalIgnoreCase).ToList();
				rebuildingLivePlayerList = true;
				players.Items.Clear();
				int num = 1;
				foreach (string item in source)
				{
					players.Items.Add($"Player {num++}: {item} - ACTIVE");
				}
				if (source.Count > 0)
				{
					clientsStatus.Text = source.Count.ToString();
					Log($"LIVE-RESOLVER {source.Count} active: {string.Join(", ", source.Select((string n, int i) => $"P{i + 1}='{n}'"))}.");
				}
				else
				{
					string value = await Task.Run(() => ReadLiveFocusName(bridge, process));
					clientsStatus.Text = "?";
					Log($"LIVE-RESOLVER returned 0 verified clients. Legacy focus 0x{63858577uL:X}='{value}' retained for diagnostics only.");
				}
				int selectedIndex = 0;
				if (!string.IsNullOrWhiteSpace(selectedName))
				{
					for (int num2 = 0; num2 < players.Items.Count; num2++)
					{
						string text = players.Items[num2]?.ToString() ?? "";
						int num3 = text.IndexOf(':');
						int num4 = text.LastIndexOf(" - ACTIVE", StringComparison.Ordinal);
						object a;
						if (num3 < 0 || num4 <= num3)
						{
							a = "";
						}
						else
						{
							int num5 = num3 + 1;
							a = text.Substring(num5, num4 - num5).Trim();
						}
						if (string.Equals((string?)a, selectedName, StringComparison.OrdinalIgnoreCase))
						{
							selectedIndex = num2;
							break;
						}
					}
				}
				if (players.Items.Count > 0)
				{
					players.SelectedIndex = selectedIndex;
				}
				rebuildingLivePlayerList = false;
				selectedLivePlayerName = GetSelectedLivePlayerName();
				if (!string.IsNullOrWhiteSpace(selectedLivePlayerName))
				{
					(int, ulong) tuple = await Task.Run(() => bridge.ResolveClientNameSlot(process, selectedLivePlayerName));
					if (debugBridge != bridge || bo2Process != process)
					{
						return;
					}
					if (tuple.Item1 >= 0)
					{
						selectedPlayerDetails.Text = $"Selected: {selectedLivePlayerName} | Active: YES | Name Slot: {tuple.Item1} | Name Address: 0x{tuple.Item2:X}";
						Log($"NAME-SLOT selected='{selectedLivePlayerName}' slot={tuple.Item1} nameAddr=0x{tuple.Item2:X} exact=YES.");
					}
					else
					{
						selectedPlayerDetails.Text = "Selected: " + selectedLivePlayerName + " | Active: YES | Name Slot: UNVERIFIED | Name Address: -";
					}
				}
				Log($"LIVE-SELECTION target='{selectedLivePlayerName}' index={players.SelectedIndex}; preserved='{selectedName ?? "<none>"}'.");
				Log("Players refresh complete. Verified resolver is authoritative; no game memory modified.");
			}
			catch (Exception ex)
			{
				rebuildingLivePlayerList = false;
				Log("Players refresh: FAIL - " + ex.GetBaseException().Message);
			}
			finally
			{
				playersRefreshInProgress = false;
			}
		}
	}

	private void CaptureLiveSelectionFromUi()
	{
		if (!rebuildingLivePlayerList)
		{
			string value = GetSelectedLivePlayerName();
			if (!string.IsNullOrWhiteSpace(value))
			{
				selectedLivePlayerName = value;
				UpdateSelectedPlayerSlotDetails();
				Log($"LIVE-SELECTION user selected '{selectedLivePlayerName}' index={players.SelectedIndex}.");
			}
		}
	}

	private void UpdateSelectedPlayerSlotDetails()
	{
		string text = GetSelectedLivePlayerName();
		if (string.IsNullOrWhiteSpace(text) || debugBridge == null || bo2Process == null)
		{
			selectedPlayerDetails.Text = "Selected: <none> | Active: NO | Name Slot: - | Name Address: -";
			return;
		}
		try
		{
			(int, ulong) tuple = debugBridge.ResolveClientNameSlot(bo2Process, text);
			if (tuple.Item1 >= 0)
			{
				selectedPlayerDetails.Text = $"Selected: {text} | Active: YES | Name Slot: {tuple.Item1} | Name Address: 0x{tuple.Item2:X}";
				Log($"NAME-SLOT selected='{text}' slot={tuple.Item1} nameAddr=0x{tuple.Item2:X} exact=YES.");
			}
			else
			{
				selectedPlayerDetails.Text = "Selected: " + text + " | Active: YES | Name Slot: UNVERIFIED | Name Address: -";
				Log("NAME-SLOT selected='" + text + "' exact match not found in verified 0x148 name table.");
			}
		}
		catch (Exception ex)
		{
			selectedPlayerDetails.Text = "Selected: " + text + " | Active: YES | Name Slot: ERROR | Name Address: -";
			Log("NAME-SLOT resolve FAIL: " + ex.GetBaseException().Message);
		}
	}

	private async Task ProbeSelectedClientMappingAsync()
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("Attach BO2 first.");
			return;
		}
		if (clientMapRunning)
		{
			Log("CLIENT-MAP scan is already running.");
			return;
		}
		string selected = GetSelectedLivePlayerName();
		if (string.IsNullOrWhiteSpace(selected))
		{
			Log("CLIENT-MAP select an ACTIVE player first.");
			return;
		}
		clientMapRunning = true;
		try
		{
			Log("CLIENT-MAP V3.7.33 targeted scan started for selected='" + selected + "'. Read-only; UI remains responsive.");
			Log("CLIENT-MAP V3.7.33 avoids the V3.7.32 full process-map scan and probes the known BO2 player-name hit set instead.");
			DebugBridge bridge = debugBridge;
			object proc = bo2Process;
			List<string> list = await Task.Run(() => bridge.AnalyzeKnownPlayerNameHits(proc, selected));
			int exact = 0;
			foreach (string item in list)
			{
				if (item.IndexOf("EXACT", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					exact++;
				}
				Log("CLIENT-MAP " + item);
				await Task.Yield();
			}
			Log($"CLIENT-MAP RESULT selected='{selected}' exactTagged={exact}. Compare P1/P2 addresses; mapping remains UNVERIFIED until a stable client-slot relationship is demonstrated.");
			Log("CLIENT-MAP V3.7.33 probe complete. Read-only; no game memory modified.");
		}
		catch (Exception ex)
		{
			Log("CLIENT-MAP probe: FAIL - " + ex.GetBaseException().Message);
		}
		finally
		{
			clientMapRunning = false;
		}
	}

	private void EnumerateClientNameSlots()
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("CLIENT-SLOTS Attach BO2 first.");
			return;
		}
		try
		{
			var (text, text2) = GetLivePlayerComparisonNames();
			Log($"CLIENT-SLOTS V3.7.37 enumeration started. P1='{text}' P2='{text2}'. Read-only.");
			foreach (string item in debugBridge.EnumerateClientNameSlots(bo2Process, text, text2))
			{
				Log("CLIENT-SLOTS " + item);
			}
			Log("CLIENT-SLOTS V3.7.37 enumeration complete. No game memory modified.");
		}
		catch (Exception ex)
		{
			Log("CLIENT-SLOTS enumeration FAIL: " + ex.GetBaseException().Message);
		}
	}

	private void CorrelateSelectedSlot()
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("SLOT-CORRELATE Attach BO2 first.");
			return;
		}
		string text = GetSelectedLivePlayerName();
		if (string.IsNullOrWhiteSpace(text))
		{
			Log("SLOT-CORRELATE select an ACTIVE player first.");
			return;
		}
		try
		{
			(int, ulong) tuple = debugBridge.ResolveClientNameSlot(bo2Process, text);
			if (tuple.Item1 < 0)
			{
				Log("SLOT-CORRELATE selected='" + text + "' has no verified name slot.");
				return;
			}
			Log($"SLOT-CORRELATE V3.7.40 started selected='{text}' verifiedNameSlot={tuple.Item1} nameAddr=0x{tuple.Item2:X}. Read-only.");
			Log("SLOT-CORRELATE Nova resource lead: BO2 lobby scripting uses GetLobbyClientCount and _clientids.gsc; this probe does not assume those engine client IDs equal our name-slot index.");
			foreach (string item in debugBridge.CorrelateNameSlotRecords(bo2Process, tuple.Item1))
			{
				Log("SLOT-CORRELATE " + item);
			}
			Log("SLOT-CORRELATE complete. No game memory modified; lobby/account index equivalence remains unverified until independently correlated.");
		}
		catch (Exception ex)
		{
			Log("SLOT-CORRELATE FAIL: " + ex.GetBaseException().Message);
		}
	}

	private void CharacterizeLiveClientRecords()
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("CLIENT-RECORD Attach BO2 first.");
			return;
		}
		try
		{
			var (text, text2) = GetLivePlayerComparisonNames();
			Log($"CLIENT-RECORD V3.7.36 scan started. P1='{text}' P2='{text2}' source=live-player-list. Read-only.");
			foreach (string item in debugBridge.CharacterizeLiveClientRecords(bo2Process, text, text2))
			{
				Log("CLIENT-RECORD " + item);
			}
			Log("CLIENT-RECORD V3.7.36 scan complete. No game memory modified.");
		}
		catch (Exception ex)
		{
			Log("CLIENT-RECORD scan FAIL: " + ex.GetBaseException().Message);
		}
	}

	private void CaptureMapWatch(string state)
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("MAP-WATCH Attach BO2 first.");
			return;
		}
		try
		{
			Dictionary<ulong, byte[]> dictionary = debugBridge.CaptureClientMappingWatch(bo2Process);
			mapWatch[state] = dictionary;
			Log($"MAP-WATCH captured state={state} regions={dictionary.Count}. Read-only.");
			foreach (KeyValuePair<ulong, byte[]> item in dictionary)
			{
				Log($"MAP-WATCH {state} region=0x{item.Key:X} bytes={item.Value.Length} preview={Convert.ToHexString(item.Value.Take(24).ToArray())}");
			}
			if (state == "P2_LEFT" && mapWatch.TryGetValue("BOTH_IN", out Dictionary<ulong, byte[]> value))
			{
				LogMapWatchDiff("BOTH_IN", "P2_LEFT", value, dictionary);
			}
			if (state == "P2_REJOIN" && mapWatch.TryGetValue("P2_LEFT", out Dictionary<ulong, byte[]> value2))
			{
				LogMapWatchDiff("P2_LEFT", "P2_REJOIN", value2, dictionary);
			}
			if (state == "P2_REJOIN" && mapWatch.TryGetValue("BOTH_IN", out Dictionary<ulong, byte[]> value3))
			{
				LogMapWatchDiff("BOTH_IN", "P2_REJOIN", value3, dictionary);
			}
		}
		catch (Exception ex)
		{
			Log("MAP-WATCH capture FAIL: " + ex.GetBaseException().Message);
		}
	}

	private void LogMapWatchDiff(string from, string to, Dictionary<ulong, byte[]> a, Dictionary<ulong, byte[]> b)
	{
		foreach (KeyValuePair<ulong, byte[]> item in a)
		{
			if (!b.TryGetValue(item.Key, out byte[] bb))
			{
				continue;
			}
			byte[] aa = item.Value;
			int num = Math.Min(aa.Length, bb.Length);
			List<int> list = new List<int>();
			for (int i = 0; i < num; i++)
			{
				if (aa[i] != bb[i])
				{
					list.Add(i);
				}
			}
			string text = ((list.Count == 0) ? "none" : string.Join(",", from num2 in list.Take(48)
				select $"+0x{num2:X}:{aa[num2]:X2}>{bb[num2]:X2}"));
			if (list.Count > 48)
			{
				text += $",...(+{list.Count - 48} more)";
			}
			Log($"MAP-WATCH DIFF {from}->{to} region=0x{item.Key:X} changed={list.Count} [{text}]");
		}
	}

	private string GetSelectedLivePlayerName()
	{
		if (!(players.SelectedItem is string text))
		{
			return "";
		}
		if (!text.EndsWith(" - ACTIVE", StringComparison.Ordinal))
		{
			return "";
		}
		int num = text.IndexOf(':');
		int num2 = text.LastIndexOf(" - ACTIVE", StringComparison.Ordinal);
		if (num < 0 || num2 <= num)
		{
			return "";
		}
		int num3 = num + 1;
		return text.Substring(num3, num2 - num3).Trim();
	}

	private (string Player1, string Player2) GetLivePlayerComparisonNames()
	{
		string[] source = (from name in players.Items.Cast<string>().Select(delegate(string text)
			{
				int num = text.IndexOf(':');
				int num2 = text.LastIndexOf(" - ACTIVE", StringComparison.Ordinal);
				if (num < 0 || num2 <= num)
				{
					return "";
				}
				int num3 = num + 1;
				return text.Substring(num3, num2 - num3).Trim();
			})
			where name.Length > 0
			select name).ToArray();
		string first = GetSelectedLivePlayerName();
		if (first.Length == 0 && !string.IsNullOrWhiteSpace(selectedLivePlayerName))
		{
			first = selectedLivePlayerName;
		}
		if (first.Length == 0)
		{
			first = source.FirstOrDefault() ?? "";
		}
		string item = source.FirstOrDefault((string name) => !string.Equals(name, first, StringComparison.OrdinalIgnoreCase)) ?? "";
		return (Player1: first, Player2: item);
	}

	private void CaptureBaseline()
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("Attach BO2 first.");
			return;
		}
		try
		{
			int num = (int)snapshotSlot.Value;
			snapshotBaseline = debugBridge.ReadPlayerRecord80(bo2Process, num);
			snapshotBaselineSlot = num;
			Log($"SELF-DIFF baseline captured: slot={num:00}, bytes={snapshotBaseline.Length}/128. Read-only; no game memory was modified.");
		}
		catch (Exception ex)
		{
			Log("Baseline capture: FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void CompareCurrent()
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("Attach BO2 first.");
			return;
		}
		if (snapshotBaseline == null || snapshotBaselineSlot < 0)
		{
			Log("Capture Baseline first.");
			return;
		}
		int num = (int)snapshotSlot.Value;
		if (num != snapshotBaselineSlot)
		{
			Log($"Snapshot slot changed. Baseline belongs to slot {snapshotBaselineSlot:00}; select that slot or capture a new baseline.");
			return;
		}
		try
		{
			byte[] array = debugBridge.ReadPlayerRecord80(bo2Process, num);
			int num2 = Math.Min(snapshotBaseline.Length, array.Length);
			List<string> list = new List<string>();
			for (int i = 0; i < num2; i++)
			{
				if (snapshotBaseline[i] != array[i])
				{
					list.Add($"0x{i:X2}:{snapshotBaseline[i]:X2}->{array[i]:X2}");
				}
			}
			Log($"SELF-DIFF slot={num:00} changedBytes={list.Count}/{num2}");
			Log("SELF-DIFF bytes=" + ((list.Count == 0) ? "none" : string.Join(",", list)));
			List<string> list2 = new List<string>();
			int num3 = -1;
			for (int j = 0; j <= num2; j++)
			{
				int num4;
				if (j < num2)
				{
					num4 = ((snapshotBaseline[j] != array[j]) ? 1 : 0);
					if (num4 != 0 && num3 < 0)
					{
						num3 = j;
					}
				}
				else
				{
					num4 = 0;
				}
				if (num4 == 0 && num3 >= 0)
				{
					list2.Add($"0x{num3:X2}-0x{j - 1:X2}({j - num3})");
					num3 = -1;
				}
			}
			Log("SELF-DIFF changed-runs=" + ((list2.Count == 0) ? "none" : string.Join(",", list2)));
			Log("SELF-DIFF compare complete. Baseline retained for repeat testing; no game memory was modified.");
		}
		catch (Exception ex)
		{
			Log("Compare current: FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void ClearBaseline()
	{
		snapshotBaseline = null;
		snapshotBaselineSlot = -1;
		Log("SELF-DIFF baseline cleared.");
	}

	private void CaptureExpandedBaseline()
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("Attach BO2 first.");
			return;
		}
		try
		{
			int num = (int)snapshotSlot.Value;
			DebugBridge.PlayerWindowRead playerWindowRead = debugBridge.ReadPlayerWindow(bo2Process, num, 2048, 2048);
			expandedBaseline = playerWindowRead.Bytes;
			expandedBaselineSlot = num;
			expandedBaselineAddress = playerWindowRead.Address;
			Log($"EXP-DIFF baseline captured: slot={num:00}, address=0x{playerWindowRead.Address:X}, bytes={playerWindowRead.Bytes.Length}/4096, playerRecordOffset=+0x{2048:X}. Read-only; no game memory was modified.");
		}
		catch (Exception ex)
		{
			Log("Expanded baseline capture: FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void CompareExpandedCurrent()
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("Attach BO2 first.");
			return;
		}
		if (expandedBaseline == null || expandedBaselineSlot < 0)
		{
			Log("Capture Expanded first.");
			return;
		}
		int num = (int)snapshotSlot.Value;
		if (num != expandedBaselineSlot)
		{
			Log($"Expanded snapshot slot changed. Baseline belongs to slot {expandedBaselineSlot:00}; select that slot or capture a new expanded baseline.");
			return;
		}
		try
		{
			DebugBridge.PlayerWindowRead snap = debugBridge.ReadPlayerWindow(bo2Process, num, 2048, 2048);
			if (snap.Address != expandedBaselineAddress)
			{
				Log($"EXP-DIFF address changed: baseline=0x{expandedBaselineAddress:X}, current=0x{snap.Address:X}. Capture a new baseline.");
				return;
			}
			int num2 = Math.Min(expandedBaseline.Length, snap.Bytes.Length);
			List<int> list = new List<int>();
			for (int i = 0; i < num2; i++)
			{
				if (expandedBaseline[i] != snap.Bytes[i])
				{
					list.Add(i);
				}
			}
			Log($"EXP-DIFF slot={num:00} changedBytes={list.Count}/{num2}");
			if (list.Count == 0)
			{
				Log("EXP-DIFF bytes=none");
			}
			else
			{
				IEnumerable<string> values = list.Take(256).Select(delegate(int num9)
				{
					long num8 = (long)num9 - 2048L;
					string value3 = ((num8 < 0) ? $"-0x{-num8:X}" : $"+0x{num8:X}");
					return $"win+0x{num9:X3}/player{value3}/abs=0x{(ulong)((long)snap.Address + (long)num9):X}:{expandedBaseline[num9]:X2}->{snap.Bytes[num9]:X2}";
				});
				Log("EXP-DIFF bytes=" + string.Join(",", values) + ((list.Count > 256) ? $",... ({list.Count - 256} more suppressed)" : ""));
			}
			List<string> list2 = new List<string>();
			int num3 = -1;
			for (int num4 = 0; num4 <= num2; num4++)
			{
				int num5;
				if (num4 < num2)
				{
					num5 = ((expandedBaseline[num4] != snap.Bytes[num4]) ? 1 : 0);
					if (num5 != 0 && num3 < 0)
					{
						num3 = num4;
					}
				}
				else
				{
					num5 = 0;
				}
				if (num5 == 0 && num3 >= 0)
				{
					long num6 = (long)num3 - 2048L;
					long num7 = (long)(num4 - 1) - 2048L;
					string value = ((num6 < 0) ? $"-0x{-num6:X}" : $"+0x{num6:X}");
					string value2 = ((num7 < 0) ? $"-0x{-num7:X}" : $"+0x{num7:X}");
					list2.Add($"player{value}..{value2}({num4 - num3})");
					num3 = -1;
				}
			}
			Log("EXP-DIFF changed-runs=" + ((list2.Count == 0) ? "none" : string.Join(",", list2.Take(128))) + ((list2.Count > 128) ? ",..." : ""));
			Log("EXP-DIFF compare complete. Baseline retained for repeat testing; no game memory was modified.");
		}
		catch (Exception ex)
		{
			Log("Expanded compare: FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void ClearExpandedBaseline()
	{
		expandedBaseline = null;
		expandedBaselineSlot = -1;
		expandedBaselineAddress = 0uL;
		Log("EXP-DIFF baseline cleared.");
	}

	private void CaptureCandidateScan()
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("Attach BO2 first.");
			return;
		}
		try
		{
			Log("SCAN-DIFF baseline capture started. Keep the lobby stable until capture completes.");
			scanBaseline = debugBridge.ReadCandidateRegions(bo2Process);
			long value = ((IEnumerable<DebugBridge.RegionSnapshot>)scanBaseline).Sum((Func<DebugBridge.RegionSnapshot, long>)((DebugBridge.RegionSnapshot x) => x.Bytes.Length));
			Log($"SCAN-DIFF baseline captured: regions={scanBaseline.Count}, bytes={value}. Read-only; no game memory was modified.");
		}
		catch (Exception ex)
		{
			Log("Candidate scan baseline: FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void CompareCandidateScan()
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("Attach BO2 first.");
			return;
		}
		if (scanBaseline == null || scanBaseline.Count == 0)
		{
			Log("Capture Candidate Scan first.");
			return;
		}
		try
		{
			Log("SCAN-DIFF comparison started. Reading the same bounded candidate regions.");
			List<DebugBridge.RegionSnapshot> list = debugBridge.ReadCandidateRegions(bo2Process);
			Dictionary<ulong, DebugBridge.RegionSnapshot> dictionary = scanBaseline.ToDictionary((DebugBridge.RegionSnapshot x) => x.Address);
			List<(ulong, byte, byte)> list2 = new List<(ulong, byte, byte)>();
			foreach (DebugBridge.RegionSnapshot item in list)
			{
				if (!dictionary.TryGetValue(item.Address, out var value))
				{
					continue;
				}
				int num = Math.Min(value.Bytes.Length, item.Bytes.Length);
				for (int num2 = 0; num2 < num; num2++)
				{
					if (value.Bytes[num2] != item.Bytes[num2])
					{
						list2.Add((item.Address + (ulong)num2, value.Bytes[num2], item.Bytes[num2]));
					}
				}
			}
			Log($"SCAN-DIFF changedBytes={list2.Count} across {list.Count} bounded regions.");
			foreach (var item2 in list2.Take(300))
			{
				Log($"SCAN-CAND abs=0x{item2.Item1:X} {item2.Item2:X2}->{item2.Item3:X2}");
			}
			if (list2.Count > 300)
			{
				Log($"SCAN-DIFF {list2.Count - 300} additional changed bytes suppressed.");
			}
			Log("SCAN-DIFF compare complete. Baseline retained for A->B->A testing; no game memory was modified.");
		}
		catch (Exception ex)
		{
			Log("Candidate scan compare: FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void ClearCandidateScan()
	{
		scanBaseline = null;
		Log("SCAN-DIFF baseline cleared.");
	}

	private void CaptureAbaA1()
	{
		CaptureAba(ref abaA1, "A1");
	}

	private void CaptureAbaB()
	{
		CaptureAba(ref abaB, "B");
	}

	private void CaptureAbaA2()
	{
		CaptureAba(ref abaA2, "A2");
	}

	private void CaptureAba(ref List<DebugBridge.RegionSnapshot>? target, string label)
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("Attach BO2 first.");
			return;
		}
		try
		{
			Log("ABA " + label + " capture started. Keep all unrelated lobby state stable.");
			target = debugBridge.ReadCandidateRegions(bo2Process);
			long value = ((IEnumerable<DebugBridge.RegionSnapshot>)target).Sum((Func<DebugBridge.RegionSnapshot, long>)((DebugBridge.RegionSnapshot x) => x.Bytes.Length));
			Log($"ABA {label} captured: regions={target.Count}, bytes={value}. Read-only; no game memory was modified.");
		}
		catch (Exception ex)
		{
			Log("ABA " + label + " capture: FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void AnalyzeAba()
	{
		if (abaA1 == null || abaB == null || abaA2 == null)
		{
			Log("ABA requires Capture A1, Capture B, and Capture A2 before analysis.");
			return;
		}
		try
		{
			abaA1.ToDictionary((DebugBridge.RegionSnapshot x) => x.Address);
			Dictionary<ulong, DebugBridge.RegionSnapshot> dictionary = abaB.ToDictionary((DebugBridge.RegionSnapshot x) => x.Address);
			Dictionary<ulong, DebugBridge.RegionSnapshot> dictionary2 = abaA2.ToDictionary((DebugBridge.RegionSnapshot x) => x.Address);
			List<(ulong, byte, byte)> list = new List<(ulong, byte, byte)>();
			int num = 0;
			foreach (DebugBridge.RegionSnapshot item in abaA1)
			{
				if (!dictionary.TryGetValue(item.Address, out var value) || !dictionary2.TryGetValue(item.Address, out var value2))
				{
					continue;
				}
				int num2 = Math.Min(item.Bytes.Length, Math.Min(value.Bytes.Length, value2.Bytes.Length));
				for (int num3 = 0; num3 < num2; num3++)
				{
					num++;
					byte b = item.Bytes[num3];
					byte b2 = value.Bytes[num3];
					byte b3 = value2.Bytes[num3];
					if (b == b3 && b2 != b)
					{
						list.Add((item.Address + (ulong)num3, b, b2));
					}
				}
			}
			Log($"ABA-CORR analyzedBytes={num}, candidates={list.Count} (A1==A2 && B!=A).");
			foreach (var item2 in list.Take(300))
			{
				Log($"ABA-CAND abs=0x{item2.Item1:X} A={item2.Item2:X2} B={item2.Item3:X2} A2={item2.Item2:X2}");
			}
			if (list.Count > 300)
			{
				Log($"ABA-CORR {list.Count - 300} additional candidates suppressed.");
			}
			Log("ABA-CORR analysis complete. Snapshots retained for review; no game memory was modified.");
		}
		catch (Exception ex)
		{
			Log("ABA analysis: FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void ClearAba()
	{
		abaA1 = null;
		abaB = null;
		abaA2 = null;
		Log("ABA snapshots cleared.");
	}

	private byte ControlledReadByte()
	{
		if (debugBridge == null || bo2Process == null)
		{
			throw new InvalidOperationException("Attach BO2 first.");
		}
		return debugBridge.ReadByte(bo2Process, ControlledAddress);
	}

	private void ControlledRead()
	{
		try
		{
			byte value = ControlledReadByte();
			Log($"CW-READ abs=0x{ControlledAddress:X} value={value:X2} allowed={ControlledLowValue:X2}/{ControlledHighValue:X2}");
		}
		catch (Exception ex)
		{
			Log("CW read: FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void ControlledSaveOriginal()
	{
		try
		{
			byte b = ControlledReadByte();
			if (b != ControlledLowValue && b != ControlledHighValue)
			{
				Log($"CW save blocked: current value {b:X2} is outside validated states {ControlledLowValue:X2}/{ControlledHighValue:X2}.");
			}
			else
			{
				controlledOriginal = b;
				controlledSavedAddress = ControlledAddress;
				Log($"CW-SAVED abs=0x{ControlledAddress:X} original={b:X2}");
			}
		}
		catch (Exception ex)
		{
			Log("CW save original: FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void ControlledWrite(byte value)
	{
		if (value != ControlledLowValue && value != ControlledHighValue)
		{
			Log($"CW blocked: only {ControlledLowValue:X2}/{ControlledHighValue:X2} permitted for this candidate.");
			return;
		}
		if (!controlledOriginal.HasValue || controlledSavedAddress != ControlledAddress)
		{
			Log("CW blocked: click Save Original for the currently selected candidate first.");
			return;
		}
		if (debugBridge == null || bo2Process == null)
		{
			Log("Attach BO2 first.");
			return;
		}
		try
		{
			byte b = ControlledReadByte();
			if (b != ControlledLowValue && b != ControlledHighValue)
			{
				Log($"CW blocked: current value {b:X2} is outside validated states.");
				return;
			}
			debugBridge.WriteByteRestricted(bo2Process, ControlledAddress, value);
			byte b2 = ControlledReadByte();
			Log($"CW-WRITE abs=0x{ControlledAddress:X} requested={value:X2} before={b:X2} readback={b2:X2} {((b2 == value) ? "VERIFY=OK" : "VERIFY=MISMATCH")}");
		}
		catch (Exception ex)
		{
			Log("CW write: FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void ControlledVerify()
	{
		try
		{
			byte value = ControlledReadByte();
			Log($"CW-VERIFY abs=0x{ControlledAddress:X} current={value:X2} savedOriginal={(controlledOriginal.HasValue ? controlledOriginal.Value.ToString("X2") : "none")}");
		}
		catch (Exception ex)
		{
			Log("CW verify: FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void ControlledRestore()
	{
		if (!controlledOriginal.HasValue || controlledSavedAddress != ControlledAddress)
		{
			Log("CW restore blocked: no saved original for the selected candidate.");
			return;
		}
		if (debugBridge == null || bo2Process == null)
		{
			Log("Attach BO2 first.");
			return;
		}
		try
		{
			debugBridge.WriteByteRestricted(bo2Process, ControlledAddress, controlledOriginal.Value);
			byte b = ControlledReadByte();
			Log($"CW-RESTORE abs=0x{ControlledAddress:X} requested={controlledOriginal.Value:X2} readback={b:X2} {((b == controlledOriginal.Value) ? "VERIFY=OK" : "VERIFY=MISMATCH")}");
		}
		catch (Exception ex)
		{
			Log("CW restore: FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void ReadValidatorNow()
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("Attach BO2 first.");
			return;
		}
		try
		{
			Dictionary<ulong, byte> source = debugBridge.ReadValidationCandidates(bo2Process);
			Log("VAL-SNAP " + string.Join(" ", from x in source
				orderby x.Key
				select $"0x{x.Key:X}={x.Value:X2}"));
			validatorLast = source;
		}
		catch (Exception ex)
		{
			Log("Validator read: FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void StartValidator()
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("Attach BO2 first.");
			return;
		}
		validatorLast = null;
		validatorRunning = true;
		validatorTimer.Start();
		Log("VAL started (500 ms, five confirmed candidates). Change only the controlled team state; read-only.");
		ValidatorTick();
	}

	private void StopValidator()
	{
		validatorRunning = false;
		validatorTimer.Stop();
		Log("VAL stopped. No game memory was modified.");
	}

	private void ValidatorTick()
	{
		if (!validatorRunning || debugBridge == null || bo2Process == null)
		{
			return;
		}
		try
		{
			Dictionary<ulong, byte> source = debugBridge.ReadValidationCandidates(bo2Process);
			if (validatorLast == null)
			{
				Log("VAL initial " + string.Join(" ", from x in source
					orderby x.Key
					select $"0x{x.Key:X}={x.Value:X2}"));
			}
			else
			{
				foreach (KeyValuePair<ulong, byte> item in source.OrderBy((KeyValuePair<ulong, byte> x) => x.Key))
				{
					if (validatorLast.TryGetValue(item.Key, out var value) && value != item.Value)
					{
						Log($"VAL-CHANGE abs=0x{item.Key:X} {value:X2}->{item.Value:X2}");
					}
				}
			}
			validatorLast = source;
		}
		catch (Exception ex)
		{
			Log("Validator: FAIL - " + ex.GetBaseException().Message);
			StopValidator();
		}
	}

	private void CaptureTransitionBaseline()
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("Attach BO2 first.");
			return;
		}
		try
		{
			transitionBaseline = debugBridge.ReadValidationCandidates(bo2Process);
			transitionLast = new Dictionary<ulong, byte>(transitionBaseline);
			Log("TRANS-BASE " + string.Join(" ", from x in transitionBaseline
				orderby x.Key
				select $"0x{x.Key:X}={x.Value:X2}"));
		}
		catch (Exception ex)
		{
			Log("Transition baseline: FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void StartTransitionWatch()
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("Attach BO2 first.");
			return;
		}
		if (transitionBaseline == null)
		{
			Log("Capture Transition Baseline first.");
			return;
		}
		transitionLast = new Dictionary<ulong, byte>(transitionBaseline);
		transitionStarted = DateTime.Now;
		transitionRunning = true;
		transitionTimer.Start();
		Log("TRANS-WATCH started (100 ms, read-only). Make ONE controlled lobby/team transition now.");
	}

	private void StopTransitionWatch()
	{
		transitionRunning = false;
		transitionTimer.Stop();
		if (debugBridge != null && bo2Process != null)
		{
			try
			{
				Dictionary<ulong, byte> source = debugBridge.ReadValidationCandidates(bo2Process);
				Log("TRANS-END " + string.Join(" ", from x in source
					orderby x.Key
					select $"0x{x.Key:X}={x.Value:X2}"));
				if (transitionBaseline != null)
				{
					foreach (KeyValuePair<ulong, byte> item in source.OrderBy((KeyValuePair<ulong, byte> x) => x.Key))
					{
						if (transitionBaseline.TryGetValue(item.Key, out var value))
						{
							Log($"TRANS-SUM abs=0x{item.Key:X} baseline={value:X2} final={item.Value:X2} {((value == item.Value) ? "UNCHANGED" : "CHANGED")}");
						}
					}
				}
			}
			catch (Exception ex)
			{
				Log("Transition stop read: FAIL - " + ex.GetBaseException().Message);
			}
		}
		Log("TRANS-WATCH stopped. No game memory was modified.");
	}

	private void ClearTransition()
	{
		transitionRunning = false;
		transitionTimer.Stop();
		transitionBaseline = null;
		transitionLast = null;
		Log("TRANS state cleared.");
	}

	private void TransitionTick()
	{
		if (!transitionRunning || debugBridge == null || bo2Process == null)
		{
			return;
		}
		try
		{
			Dictionary<ulong, byte> source = debugBridge.ReadValidationCandidates(bo2Process);
			if (transitionLast != null)
			{
				long value = (long)(DateTime.Now - transitionStarted).TotalMilliseconds;
				foreach (KeyValuePair<ulong, byte> item in source.OrderBy((KeyValuePair<ulong, byte> x) => x.Key))
				{
					if (transitionLast.TryGetValue(item.Key, out var value2) && value2 != item.Value)
					{
						Log($"TRANS-EVENT t=+{value}ms abs=0x{item.Key:X} {value2:X2}->{item.Value:X2}");
					}
				}
			}
			transitionLast = source;
		}
		catch (Exception ex)
		{
			Log("Transition watch: FAIL - " + ex.GetBaseException().Message);
			StopTransitionWatch();
		}
	}

	private void Stub(object? s, EventArgs e)
	{
		Log("This control is a placeholder in the test skeleton.");
	}

	private void Log(string s)
	{
		log.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}{Environment.NewLine}");
	}

	private void LogQuiet(string s)
	{
		if ((DateTime.Now - lastQuiet).TotalSeconds >= 10.0)
		{
			Log(s);
			lastQuiet = DateTime.Now;
		}
	}

	private static FlowLayoutPanel Stack()
	{
		return new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.TopDown,
			WrapContents = false,
			AutoScroll = true,
			Padding = new Padding(22, 20, 22, 24),
			BackColor = CleanBg
		};
	}

	private static FlowLayoutPanel Row(params Control[] cs)
	{
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel();
		flowLayoutPanel.AutoSize = true;
		flowLayoutPanel.FlowDirection = FlowDirection.LeftToRight;
		flowLayoutPanel.WrapContents = true;
		flowLayoutPanel.Margin = new Padding(0, 0, 0, 12);
		flowLayoutPanel.BackColor = Color.Transparent;
		flowLayoutPanel.Controls.AddRange(cs);
		return flowLayoutPanel;
	}

	private static Button Btn(string text, EventHandler h)
	{
		PremiumButton premiumButton = new PremiumButton();
		premiumButton.Text = text;
		premiumButton.AutoSize = true;
		premiumButton.Click += h;
		return premiumButton;
	}

	private void HookOptionSounds(Control parent)
	{
		foreach (Control control in parent.Controls)
		{
			if (control is ComboBox comboBox)
			{
				comboBox.SelectionChangeCommitted += delegate
				{
					PlayClickSound();
				};
			}
			else if ((control is CheckBox || control is RadioButton || control is NumericUpDown || control is TrackBar || control is ListBox) ? true : false)
			{
				control.MouseDown += delegate(object? _, MouseEventArgs e)
				{
					if (e.Button == MouseButtons.Left && control.Enabled)
					{
						PlayClickSound();
					}
				};
			}
			if (control.HasChildren)
			{
				HookOptionSounds(control);
			}
		}
	}

	private static Label StatusLabel(string t)
	{
		return new Label
		{
			Text = t,
			AutoSize = true
		};
	}

	private static Control StatusRow(string name, Control value)
	{
		return Row(new Label
		{
			Text = name.ToUpperInvariant(),
			AutoSize = true,
			Width = 126,
			ForeColor = CleanTextMuted,
			Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
			TextAlign = ContentAlignment.MiddleLeft,
			Margin = new Padding(0, 7, 0, 7)
		}, value);
	}

	private static Control Note(string t)
	{
		return new InfoBar(t)
		{
			Margin = new Padding(0, 4, 0, 14)
		};
	}

	private void CaptureSlotFieldWatch(string state)
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("SLOT-WATCH Attach BO2 first.");
			return;
		}
		try
		{
			byte[] array = debugBridge.ReadNameSlotPair(bo2Process);
			if (array.Length < 656)
			{
				Log($"SLOT-WATCH {state} short read={array.Length}; expected=656.");
				return;
			}
			switch (state)
			{
			case "BOTH_IN":
				slotFieldBothIn = array;
				break;
			case "P2_LEFT":
				slotFieldP2Left = array;
				break;
			case "P2_REJOIN":
				slotFieldP2Rejoin = array;
				break;
			}
			Log($"SLOT-WATCH captured state={state} bytes={array.Length}. Read-only.");
			LogSlotFieldState(state, array);
			if (state == "P2_LEFT" && slotFieldBothIn != null)
			{
				LogSlotFieldDiff("BOTH_IN->P2_LEFT", slotFieldBothIn, array);
			}
			if (state == "P2_REJOIN")
			{
				if (slotFieldP2Left != null)
				{
					LogSlotFieldDiff("P2_LEFT->P2_REJOIN", slotFieldP2Left, array);
				}
				if (slotFieldBothIn != null)
				{
					LogSlotFieldDiff("BOTH_IN->P2_REJOIN", slotFieldBothIn, array);
				}
			}
		}
		catch (Exception ex)
		{
			Log("SLOT-WATCH FAIL: " + ex.GetBaseException().Message);
		}
	}

	private void LogSlotFieldState(string state, byte[] data)
	{
		int[] source = new int[11]
		{
			92, 95, 100, 115, 143, 145, 148, 152, 184, 188,
			228
		};
		for (int i = 0; i < 2; i++)
		{
			int b = i * 328;
			int j;
			for (j = 0; j < 32 && data[b + j] >= 32 && data[b + j] <= 126; j++)
			{
			}
			string value = ((j == 0) ? "" : Encoding.ASCII.GetString(data, b, j));
			Log($"SLOT-WATCH {state} slot={i} name='{value}' " + string.Join(" ", source.Select((int o) => $"+0x{o:X2}=0x{data[b + o]:X2}")));
		}
	}

	private void LogSlotFieldDiff(string label, byte[] before, byte[] after)
	{
		int[] source = new int[11]
		{
			92, 95, 100, 115, 143, 145, 148, 152, 184, 188,
			228
		};
		for (int i = 0; i < 2; i++)
		{
			int b = i * 328;
			string[] array = (from o in source
				where before[b + o] != after[b + o]
				select $"+0x{o:X2}:{before[b + o]:X2}>{after[b + o]:X2}").ToArray();
			Log($"SLOT-WATCH DIFF {label} slot={i} candidateChanges={array.Length} [{string.Join(",", array)}]");
		}
	}

	private void LinkedRecordWatch(string state)
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("LINK-WATCH Attach BO2 first.");
			return;
		}
		try
		{
			Log("LINK-WATCH V3.7.45 capture requested state=" + state + ". Read-only.");
			foreach (string item in debugBridge.CaptureLinkedRecordWatch(bo2Process, state))
			{
				Log("LINK-WATCH " + item);
			}
			Log("LINK-WATCH capture complete. No game memory modified.");
		}
		catch (Exception ex)
		{
			Log("LINK-WATCH FAIL: " + ex.GetBaseException().Message);
		}
	}

	private void TraceVerifiedNameTable()
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("NAME-TRACE Attach BO2 first.");
			return;
		}
		try
		{
			Log("NAME-TRACE V3.7.44 started. Verified 0x148 name-table anchor trace is read-only.");
			foreach (string item in debugBridge.TraceVerifiedNameTable(bo2Process))
			{
				Log("NAME-TRACE " + item);
			}
			Log("NAME-TRACE complete. No game memory modified.");
		}
		catch (Exception ex)
		{
			Log("NAME-TRACE FAIL: " + ex.GetBaseException().Message);
		}
	}

	private void FindBoundedClientBridge()
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("BRIDGE-FIND Attach BO2 first.");
			return;
		}
		try
		{
			Log("BRIDGE-FIND V3.7.43 started. Bounded read-only search around resolved client pointer.");
			foreach (string item in debugBridge.FindBoundedClientBridge(bo2Process))
			{
				Log("BRIDGE-FIND " + item);
			}
			Log("BRIDGE-FIND complete. No game memory modified.");
		}
		catch (Exception ex)
		{
			Log("BRIDGE-FIND FAIL: " + ex.GetBaseException().Message);
		}
	}

	private void CorrelateClientBase()
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("CLIENT-CORRELATE Attach BO2 first.");
			return;
		}
		try
		{
			Log("CLIENT-CORRELATE V3.7.42 started. Name-slot/client-base comparison is read-only.");
			foreach (string item in debugBridge.CorrelateNameSlotsToClientBase(bo2Process))
			{
				Log("CLIENT-CORRELATE " + item);
			}
			Log("CLIENT-CORRELATE complete. No game memory modified.");
		}
		catch (Exception ex)
		{
			Log("CLIENT-CORRELATE FAIL: " + ex.GetBaseException().Message);
		}
	}

	private void CompareOccupiedSlotFields()
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("SLOT-FIELDS Attach BO2 first.");
			return;
		}
		try
		{
			Log("SLOT-FIELDS V3.7.41 comparison started. Comparing occupied 0x148 name-table records. Read-only.");
			foreach (string item in debugBridge.CompareOccupiedNameSlotFields(bo2Process))
			{
				Log("SLOT-FIELDS " + item);
			}
			Log("SLOT-FIELDS complete. No game memory modified.");
		}
		catch (Exception ex)
		{
			Log("SLOT-FIELDS FAIL: " + ex.GetBaseException().Message);
		}
	}

	private void ValidatePlayerResolver()
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("PLAYER-RESOLVER Attach BO2 first.");
			return;
		}
		try
		{
			var (text, text2) = GetLivePlayerComparisonNames();
			Log($"PLAYER-RESOLVER V3.7.47 started. P1='{text}' P2='{text2}'. Full verified chain validation is read-only.");
			foreach (string item in debugBridge.ValidateVerifiedPlayerResolver(bo2Process, text, text2))
			{
				Log("PLAYER-RESOLVER " + item);
			}
			Log("PLAYER-RESOLVER complete. No game memory modified.");
		}
		catch (Exception ex)
		{
			Log("PLAYER-RESOLVER FAIL: " + ex.GetBaseException().Message);
		}
	}

	private void CharacterizeLinkedRecordTail()
	{
		if (debugBridge == null || bo2Process == null)
		{
			Log("LINK-TAIL Attach BO2 first.");
			return;
		}
		try
		{
			Log("LINK-TAIL V3.7.46 started. Characterizing validated 0x1E0 linked-record tail. Read-only.");
			foreach (string item in debugBridge.CharacterizeLinkedRecordTail(bo2Process))
			{
				Log("LINK-TAIL " + item);
			}
			Log("LINK-TAIL complete. No game memory modified.");
		}
		catch (Exception ex)
		{
			Log("LINK-TAIL FAIL: " + ex.GetBaseException().Message);
		}
	}

	private void SendRecoveredProfileBatchPaced(string feature, IEnumerable<string> commands, int delayMs)
	{
		try
		{
			(string, int) tuple = RequireSelectedWritablePlayer();
			List<string> list = commands.ToList();
			BeginModificationProgress(feature, list.Count);
			Log($"LAN-{feature}-PACED-START target='{tuple.Item1}' selectedClient={tuple.Item2} count={list.Count} delay={delayMs}ms.");
			int num = 0;
			foreach (string item in list)
			{
				string text = WrapLobbyCommand(item);
				int num2 = Encoding.ASCII.GetByteCount(text) + 1;
				if (num2 > 512)
				{
					throw new InvalidOperationException($"Command {num + 1} exceeds recovered 512-byte limit.");
				}
				CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, tuple.Item2, text);
				if (!cbufSendResult.SignatureOk || !cbufSendResult.BufferReadbackOk)
				{
					throw new InvalidOperationException($"Command {num + 1} transport verification failed.");
				}
				num++;
				Log($"LAN-{feature}-ITEM PASS {num}/{list.Count} bytes={num2} return=0x{cbufSendResult.ReturnValue:X} cmd='{item}'.");
				AdvanceModificationProgress(feature, num, list.Count);
				Application.DoEvents();
				if (num < list.Count)
				{
					Thread.Sleep(delayMs);
				}
			}
			MarkModificationApplied(feature);
			Log($"LAN-{feature}-PACED PASS target='{tuple.Item1}' selectedClient={tuple.Item2}. Save, leave/rejoin, verify.");
		}
		catch (Exception ex)
		{
			Log("LAN-" + feature + "-PACED FAIL - " + ex.GetBaseException().Message);
		}
	}

	private static IEnumerable<string> CallingCardRecoveryCommands()
	{
		foreach (string item in RecoveryCommands.Where((string x) => x.StartsWith("statWriteDDL emblemstats ", StringComparison.OrdinalIgnoreCase)))
		{
			yield return item;
		}
		foreach (string item2 in RecoveryCommands.Where((string x) => (x.IndexOf(" statvalue ", StringComparison.OrdinalIgnoreCase) >= 0 || x.IndexOf(" challengevalue ", StringComparison.OrdinalIgnoreCase) >= 0) && (x.StartsWith("statWriteDDL itemstats ", StringComparison.OrdinalIgnoreCase) || x.StartsWith("statWriteDDL groupstats ", StringComparison.OrdinalIgnoreCase) || x.StartsWith("statWriteDDL playerstatslist ", StringComparison.OrdinalIgnoreCase) || x.StartsWith("statWriteDDL playerstatsbygametype ", StringComparison.OrdinalIgnoreCase))))
		{
			yield return item2;
		}
		foreach (string item3 in RecoveryCommands.Where((string x) => x.StartsWith("statWriteDDL unlocks ", StringComparison.OrdinalIgnoreCase)))
		{
			yield return item3;
		}
		yield return "statSetByName allEmblemsUnlocked 1";
		yield return "statSetByName allEmblemsPurchased 1";
	}

	private static IEnumerable<string> TenClassRepairCommands()
	{
		for (int i = 1; i <= 5; i++)
		{
			yield return $"statWriteDDL prestigeTokens {i} tokenSpent 1";
			yield return $"statWriteDDL prestigeTokens {i} tokenType PRESTIGE_EXTRA_CAC 1";
		}
	}

	private static IEnumerable<string> WeaponCamoRecoveryCommands()
	{
		foreach (string item in RecoveryCommands.Where((string x) => x.StartsWith("statWriteDDL itemstats ", StringComparison.OrdinalIgnoreCase)))
		{
			yield return item;
		}
		foreach (string item2 in RecoveryCommands.Where((string x) => x.StartsWith("statWriteDDL groupstats ", StringComparison.OrdinalIgnoreCase)))
		{
			yield return item2;
		}
		yield return "statSetByName allItemsUnlocked 1";
		yield return "statSetByName allItemsPurchased 1";
	}

	private static string WrapCommittedLobbyCommands(string packedInner)
	{
		return "ui_gametype \"dm;;" + packedInner + ";updategamerprofile;uploadStats;ui_gametype tdm\";xpartyswitchlobbies;ui_gametype tdm";
	}

	private static List<string> PackRecoveryWrappers(IEnumerable<string> commands)
	{
		List<string> list = new List<string>();
		List<string> list2 = new List<string>();
		foreach (string command in commands)
		{
			string text = (command ?? "").Trim();
			if (text.Length == 0)
			{
				continue;
			}
			string s = WrapCommittedLobbyCommands(string.Join(";", list2.Append(text)));
			if (Encoding.ASCII.GetByteCount(s) + 1 <= 512)
			{
				list2.Add(text);
				continue;
			}
			if (list2.Count == 0)
			{
				throw new InvalidOperationException("Single recovery command exceeds 512-byte wrapper limit: " + text);
			}
			list.Add(WrapCommittedLobbyCommands(string.Join(";", list2)));
			list2.Clear();
			list2.Add(text);
			s = WrapCommittedLobbyCommands(text);
			if (Encoding.ASCII.GetByteCount(s) + 1 <= 512)
			{
				continue;
			}
			throw new InvalidOperationException("Single recovery command exceeds 512-byte wrapper limit: " + text);
		}
		if (list2.Count > 0)
		{
			list.Add(WrapCommittedLobbyCommands(string.Join(";", list2)));
		}
		return list;
	}

	private void PreviewPackedRecovery(string feature, IEnumerable<string> commands)
	{
		try
		{
			List<string> list = commands.ToList();
			List<string> list2 = PackRecoveryWrappers(list);
			preparedRecoveryWrappers[feature] = list2;
			int value = ((list2.Count != 0) ? list2.Max((string w) => Encoding.ASCII.GetByteCount(w) + 1) : 0);
			Log($"{feature} PREVIEW: Prepared {list.Count} unlock command(s) in {list2.Count} lobby wrapper(s). Nothing has been sent yet. maxWrapper={value} bytes.");
			modificationStatus.Text = $"{feature}: PREPARED {list.Count} commands / {list2.Count} wrappers - APPLY when ready";
		}
		catch (Exception ex)
		{
			Log(feature + " PREVIEW FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void ResponsiveValidationDelay(int milliseconds)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (stopwatch.ElapsedMilliseconds < milliseconds)
		{
			Application.DoEvents();
			int num = milliseconds - (int)stopwatch.ElapsedMilliseconds;
			if (num > 0)
			{
				Thread.Sleep(Math.Min(50, num));
			}
		}
	}

	private void RunTenClassRepairTest()
	{
		try
		{
			(string, int) tuple = RequireSelectedWritablePlayer();
			if (MessageBox.Show($"Apply the recovered 10-Class EXTRA_CAC repair to the selected player?\n\nTarget: {tuple.Item1}\nClient: {tuple.Item2}\n\nThis writes five completed PRESTIGE_EXTRA_CAC purchase records (prestigeTokens 1-5), then saves the profile. Test on the fresh account first.", "Unlock 10 Classes", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
			{
				return;
			}
			List<string> list = new List<string>();
			for (int i = 1; i <= 5; i++)
			{
				list.Add(WrapCommittedLobbyCommands($"statWriteDDL prestigeTokens {i} tokenSpent 1;statWriteDDL prestigeTokens {i} tokenType PRESTIGE_EXTRA_CAC 1"));
			}
			BeginModificationProgress("TEN-CLASS", list.Count);
			Log($"TEN-CLASS START target='{tuple.Item1}' selectedClient={tuple.Item2} wrappers={list.Count}. FINAL_MASTER mapping: prestigeTokens[1..5], tokenSpent=1, tokenType=PRESTIGE_EXTRA_CAC/1.");
			for (int j = 0; j < list.Count; j++)
			{
				int num = Encoding.ASCII.GetByteCount(list[j]) + 1;
				if (num > 512)
				{
					throw new InvalidOperationException($"10-Class wrapper {j + 1} exceeds 512-byte limit.");
				}
				CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, tuple.Item2, list[j]);
				if (!cbufSendResult.SignatureOk || !cbufSendResult.BufferReadbackOk)
				{
					throw new InvalidOperationException($"10-Class wrapper {j + 1}/5 transport verification failed.");
				}
				Log($"TEN-CLASS ITEM PASS {j + 1}/5 prestigeEntry={j + 1} bytes={num} return=0x{cbufSendResult.ReturnValue:X}.");
				AdvanceModificationProgress("TEN-CLASS", j + 1, 5);
				ResponsiveValidationDelay(1200);
			}
			SaveSelectedPlayerProfile();
			MarkModificationApplied("TEN-CLASS");
			modificationStatus.Text = "10-CLASS SENT + SAVED - LEAVE/REJOIN AND COUNT CLASSES";
			Log("TEN-CLASS COMPLETE. Have the retail player leave/rejoin, open Create-a-Class, and verify whether all 10 slots are available. Do not run 100% Recovery before checking this result.");
		}
		catch (Exception ex)
		{
			Log("TEN-CLASS FAIL - " + ex.GetBaseException().Message);
		}
	}

	private void RunFullRecoveryProvenPaths()
	{
		try
		{
			(string, int) tuple = RequireSelectedWritablePlayer();
			Log($"V6.32 100% RECOVERY START target='{tuple.Item1}' selectedClient={tuple.Item2}. Trophies remain separate. Unlock 10 Classes is now included because the 1-5 EXTRA_CAC path passed live testing.");
			Log("V6.23 PHASE 1/6: Prestige Master + Level 55 / XP.");
			ApplyPrestigeStat(11m);
			SendRecoveredProfileBatch("RECOVERY-LEVEL-55", new string[2]
			{
				"statSetByName rank 55",
				$"statSetByName rankxp {Bo2LevelXp[55]}"
			});
			SaveSelectedPlayerProfile();
			ResponsiveValidationDelay(1500);
			Log("V6.23 PHASE 2/6: Weapons + Camos using the live-proven category routine.");
			PreviewPackedRecovery("WEAPONS-CAMOS", WeaponCamoRecoveryCommands());
			ApplyPreparedRecovery("WEAPONS-CAMOS");
			SaveSelectedPlayerProfile();
			ResponsiveValidationDelay(2000);
			Log("V6.23 PHASE 3/6: Max ALL Weapon Levels.");
			SendRecoveredProfileBatchSafe("WEAPON-MAX-ALL", from k in WeaponMaxXp
				orderby k.Key
				select $"statWriteDDL itemstats {k.Key} xp {k.Value}", 10, 150, 1500);
			SaveSelectedPlayerProfile();
			ResponsiveValidationDelay(1500);
			Log("V6.23 PHASE 4/6: Calling Cards + Emblems using the live-proven category routine.");
			PreviewPackedRecovery("CALLING-CARDS", CallingCardRecoveryCommands());
			ApplyPreparedRecovery("CALLING-CARDS");
			SaveSelectedPlayerProfile();
			ResponsiveValidationDelay(2000);
			Log("V6.32 PHASE 5/6: Normal Stats + 22 Game Modes + Combat medals + Scorestreak medals (StatValue + ChallengeValue).");
			ApplyNormalStatsPreset();
			SaveSelectedPlayerProfile();
			ResponsiveValidationDelay(1200);
			Log("V6.23 PHASE 6/6: Unlock 10 Classes using the live-confirmed prestigeEntries 1-5 path.");
			RunTenClassRepairTest();
			Log("V6.32 100% RECOVERY COMPLETE. Trophies remain standalone. Leave/rejoin and verify.");
			modificationStatus.Text = "V6.32 100% RECOVERY COMPLETE - VERIFY IN BO2";
		}
		catch (Exception ex)
		{
			Log("V6.32 100% RECOVERY STOP - " + ex.GetBaseException().Message + ". No automatic retry.");
		}
	}

	private void RunFullRecoveryValidation()
	{
		try
		{
			(string, int) tuple = RequireSelectedWritablePlayer();
			List<string> list = PackRecoveryWrappers(WeaponCamoRecoveryCommands());
			List<string> list2 = PackRecoveryWrappers(CallingCardRecoveryCommands());
			List<string> list3 = new List<string>();
			for (int i = 0; i < 5; i++)
			{
				list3.Add(WrapCommittedLobbyCommands($"statWriteDDL prestigeTokens {i} tokenSpent 1;statWriteDDL prestigeTokens {i} tokenType PRESTIGE_EXTRA_CAC 1"));
			}
			if (list.Count != 111)
			{
				throw new InvalidOperationException($"Weapons/Camos wrapper count mismatch: expected 111, got {list.Count}.");
			}
			if (list2.Count == 0)
			{
				throw new InvalidOperationException("Calling Cards recovery produced no wrappers.");
			}
			(string, List<string>)[] obj = new(string, List<string>)[3]
			{
				("WEAPONS-CAMOS", list),
				("CALLING-CARDS", list2),
				("TEN-CLASS", list3)
			};
			int num = obj.Sum(((string Name, List<string> Wrappers) x) => x.Wrappers.Count);
			ulong value = debugBridge.PrepareCbufRpcSession(bo2Process);
			BeginModificationProgress("V6.14-FULL", num);
			Log($"V6.14 FULL START target='{tuple.Item1}' selectedClient={tuple.Item2}; weapons=111 wrappers; callingCards={list2.Count} wrappers/{CallingCardRecoveryCommands().Count()} commands; tenClass=5 wrappers; total={num}; spacing={1500}ms; RPC stub=0x{value:X} prepared ONCE.");
			int num2 = 0;
			(string, List<string>)[] array = obj;
			for (int num3 = 0; num3 < array.Length; num3++)
			{
				(string, List<string>) tuple2 = array[num3];
				Log($"V6.14 PHASE {tuple2.Item1} START wrappers={tuple2.Item2.Count} stub=0x{value:X}.");
				for (int num4 = 0; num4 < tuple2.Item2.Count; num4++)
				{
					string text = tuple2.Item2[num4];
					int value2 = Encoding.ASCII.GetByteCount(text) + 1;
					CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, tuple.Item2, text);
					if (!cbufSendResult.SignatureOk || !cbufSendResult.BufferReadbackOk)
					{
						throw new InvalidOperationException($"{tuple2.Item1} wrapper {num4 + 1}/{tuple2.Item2.Count} transport verification failed.");
					}
					num2++;
					Log($"V6.14 {tuple2.Item1} PASS {num4 + 1}/{tuple2.Item2.Count} overall={num2}/{num} stub=0x{value:X} bytes={value2} signature=PASS bufferReadback=PASS return=0x{cbufSendResult.ReturnValue:X}.");
					AdvanceModificationProgress("V6.14-FULL", num2, num);
					Application.DoEvents();
					if (num2 < num)
					{
						ResponsiveValidationDelay(1500);
					}
				}
				Log($"V6.14 PHASE {tuple2.Item1} COMPLETE. RPC session remains 0x{value:X}.");
			}
			MarkModificationApplied("V6.14-FULL");
			Log("V6.14 FULL HARD STOP: all 343 wrappers dispatched through one prepared RPC session. Profile commits are embedded in every wrapper. Do NOT press again. Verify Weapons/Camos, Calling Cards, and all 10 Create-a-Class slots in BO2; then send Diagnostics.");
			modificationStatus.Text = "V6.14 FULL: 343 WRAPPERS SENT - VERIFY IN BO2 / SEND DIAGNOSTICS";
		}
		catch (Exception ex)
		{
			debugBridge?.InvalidateCbufRpcSession();
			Log("V6.14 FULL STOP - " + ex.GetBaseException().Message + ". RPC session invalidated; NO automatic retry.");
			modificationStatus.Text = "V6.14 FULL: STOPPED - NO RETRY / SEND DIAGNOSTICS";
		}
	}

	private void RunSingleWrapperRpcValidation()
	{
		try
		{
			(string, int) tuple = RequireSelectedWritablePlayer();
			string text = WrapCommittedLobbyCommands("ui_gametype tdm");
			int value = Encoding.ASCII.GetByteCount(text) + 1;
			Log($"V6.13.4 111-WRAPPER START target='{tuple.Item1}' selectedClient={tuple.Item2}; bytes={value}. Exactly {111} wrappers will be dispatched through one prepared RPC session at {1500}ms spacing.");
			ulong value2 = debugBridge.PrepareCbufRpcSession(bo2Process);
			Log($"V6.13.4 RPC SESSION READY pid={GetProcessPidForLog(bo2Process)} stub=0x{value2:X}. InstallRPC prepared once before all {111} dispatches.");
			for (int i = 1; i <= 111; i++)
			{
				Application.DoEvents();
				Log($"V6.13.4 WRAPPER {i}/{111} DISPATCH using prepared stub=0x{value2:X}.");
				CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, tuple.Item2, text);
				if (!cbufSendResult.SignatureOk || !cbufSendResult.BufferReadbackOk)
				{
					throw new InvalidOperationException($"Wrapper {i}/{111} transport verification failed.");
				}
				Log($"V6.13.4 WRAPPER {i}/{111} PASS stub=0x{value2:X} selectedClient={tuple.Item2}; signature=PASS bufferReadback=PASS remote=0x{cbufSendResult.RemoteCommandBuffer:X} return=0x{cbufSendResult.ReturnValue:X}.");
				Application.DoEvents();
				if (i < 111)
				{
					ResponsiveValidationDelay(1500);
				}
			}
			Log("V6.13.4 HARD STOP: 111 wrappers dispatched through one prepared RPC session. Do NOT press again. Confirm BO2 is still running and send Diagnostics.");
			modificationStatus.Text = "V6.13.4: 111 WRAPPERS SENT - HARD STOP / SEND DIAGNOSTICS";
		}
		catch (Exception ex)
		{
			debugBridge?.InvalidateCbufRpcSession();
			Log("V6.13.4 111-WRAPPER STOP - " + ex.GetBaseException().Message + ". RPC session invalidated; no retry was attempted.");
			modificationStatus.Text = "V6.13.4: TEST STOPPED - NO RETRY";
		}
	}

	private static int GetProcessPidForLog(object process)
	{
		Type type = process.GetType();
		string[] array = new string[5] { "pid", "Pid", "PID", "processid", "ProcessId" };
		foreach (string name in array)
		{
			PropertyInfo property = type.GetProperty(name);
			if (property != null)
			{
				try
				{
					return Convert.ToInt32(property.GetValue(process));
				}
				catch
				{
				}
			}
			FieldInfo field = type.GetField(name);
			if (field != null)
			{
				try
				{
					return Convert.ToInt32(field.GetValue(process));
				}
				catch
				{
				}
			}
		}
		return -1;
	}

	private void ApplyPreparedRecovery(string feature)
	{
		try
		{
			(string, int) tuple = RequireSelectedWritablePlayer();
			if (!preparedRecoveryWrappers.TryGetValue(feature, out List<string> value) || value.Count == 0)
			{
				throw new InvalidOperationException("Preview " + feature + " first. Nothing prepared.");
			}
			ulong value2 = debugBridge.PrepareCbufRpcSession(bo2Process);
			BeginModificationProgress(feature, value.Count);
			Log($"{feature} APPLY START target='{tuple.Item1}' selectedClient={tuple.Item2}; wrappers={value.Count}; spacing=1000ms; RPC session=0x{value2:X} (reused across queue).");
			for (int i = 0; i < value.Count; i++)
			{
				string text = value[i];
				int value3 = Encoding.ASCII.GetByteCount(text) + 1;
				CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, tuple.Item2, text);
				if (!cbufSendResult.SignatureOk || !cbufSendResult.BufferReadbackOk)
				{
					throw new InvalidOperationException($"Wrapper {i + 1} transport verification failed.");
				}
				Log($"{feature}: Applying option packet {i + 1} of {value.Count} to PS4 local client {tuple.Item2}. bytes={value3} return=0x{cbufSendResult.ReturnValue:X}.");
				AdvanceModificationProgress(feature, i + 1, value.Count);
				Application.DoEvents();
				if (i + 1 < value.Count)
				{
					Thread.Sleep(1000);
				}
			}
			MarkModificationApplied(feature);
			Log(feature + ": Selected option was sent to PS4. Wrapper queue complete; verify in BO2 before any additional recovery.");
		}
		catch (Exception ex)
		{
			Log(feature + " APPLY STOP - " + ex.GetBaseException().Message + ". No automatic retry; verify BO2/libdebug before applying again.");
		}
	}

	private void PreviewTenClassResearch()
	{
		Log("10-CLASS PREVIEW ONLY: old PRESTIGE_EXTRA_CAC prestigeTokens[1..5] candidate removed after live failure. BO2 exposes five base customclass slots and five prestigeclass slots, but the recovered files used so far do not prove the exact entitlement/count write that enables prestigeclass slots. Nothing sent.");
		modificationStatus.Text = "10-CLASS: RESEARCH PREVIEW ONLY - NO WRITE SENT";
	}

	private void RecoverNextWeaponCamoGroup()
	{
		try
		{
			List<int> list = (from result in (from num2 in RecoveryCommands.Where((string x) => x.StartsWith("statWriteDDL itemstats ", StringComparison.OrdinalIgnoreCase)).Select(delegate(string x)
					{
						string[] array = x.Split(' ', StringSplitOptions.RemoveEmptyEntries);
						int result;
						return (array.Length <= 2 || !int.TryParse(array[2], out result)) ? (-1) : result;
					})
					where num2 >= 0
					select num2).Distinct()
				orderby result
				select result).ToList();
			if (list.Count == 0)
			{
				Log("LAN-WEAPONS-CAMOS-BY-WEAPON: no recovered itemstats groups.");
				return;
			}
			if (nextWeaponRecoveryIndex >= list.Count)
			{
				nextWeaponRecoveryIndex = 0;
			}
			int id = list[nextWeaponRecoveryIndex];
			string value2;
			string value = (WeaponNames.TryGetValue(id, out value2) ? value2 : $"Item {id}");
			List<string> list2 = RecoveryCommands.Where((string x) => x.StartsWith($"statWriteDDL itemstats {id} ", StringComparison.OrdinalIgnoreCase)).ToList();
			Log($"LAN-WEAPONS-CAMOS-BY-WEAPON START {nextWeaponRecoveryIndex + 1}/{list.Count}: {value} ({id}), commands={list2.Count}. This run will HARD STOP after this weapon.");
			if (SendRecoveredProfileBatchOneGroup($"WEAPON-{id}-{value}", list2, 250, 2000))
			{
				nextWeaponRecoveryIndex++;
				if (nextWeaponRecoveryIndex < list.Count)
				{
					int num = list[nextWeaponRecoveryIndex];
					string value4;
					string value3 = (WeaponNames.TryGetValue(num, out value4) ? value4 : $"Item {num}");
					modificationStatus.Text = $"{value} COMPLETE - NEXT {value3} ({num})";
					Log($"LAN-WEAPONS-CAMOS-BY-WEAPON HARD STOP: {value} complete. Verify BO2/libdebug, then press Recover ALL Weapons + Camos for NEXT: {value3} ({num}).");
				}
				else
				{
					nextWeaponRecoveryIndex = 0;
					modificationStatus.Text = "WEAPONS/CAMOS ALL WEAPON GROUPS COMPLETE";
					Log("LAN-WEAPONS-CAMOS-BY-WEAPON: ALL recovered weapon groups complete. Save, leave/rejoin, verify.");
				}
			}
		}
		catch (Exception ex)
		{
			Log("LAN-WEAPONS-CAMOS-BY-WEAPON STOP - " + ex.GetBaseException().Message);
		}
	}

	private bool SendRecoveredProfileBatchOneGroup(string feature, IEnumerable<string> commands, int commandDelayMs, int groupCooldownMs)
	{
		try
		{
			(string, int) tuple = RequireSelectedWritablePlayer();
			List<string> list = commands.ToList();
			if (list.Count == 0)
			{
				Log("LAN-" + feature + ": no commands.");
				return false;
			}
			BeginModificationProgress(feature, list.Count);
			for (int i = 0; i < list.Count; i++)
			{
				string text = list[i];
				string text2 = WrapLobbyCommand(text);
				int num = Encoding.ASCII.GetByteCount(text2) + 1;
				if (num > 512)
				{
					throw new InvalidOperationException($"Command {i + 1} exceeds recovered 512-byte limit.");
				}
				CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, tuple.Item2, text2);
				if (!cbufSendResult.SignatureOk || !cbufSendResult.BufferReadbackOk)
				{
					throw new InvalidOperationException($"Command {i + 1} transport verification failed.");
				}
				Log($"LAN-{feature} PASS {i + 1}/{list.Count} bytes={num} return=0x{cbufSendResult.ReturnValue:X} cmd='{text}'.");
				AdvanceModificationProgress(feature, i + 1, list.Count);
				Application.DoEvents();
				if (i + 1 < list.Count)
				{
					Thread.Sleep(commandDelayMs);
				}
			}
			if (groupCooldownMs > 0)
			{
				Thread.Sleep(groupCooldownMs);
			}
			MarkModificationApplied(feature);
			return true;
		}
		catch (Exception ex)
		{
			Log($"LAN-{feature} STOP - {ex.GetBaseException().Message}. Weapon group NOT advanced; reconnect/verify and press the same button to retry this weapon only.");
			return false;
		}
	}

	private void SendRecoveredProfileBatchSafe(string feature, IEnumerable<string> commands, int chunkSize, int commandDelayMs, int chunkPauseMs, int sessionSize = 0, int sessionPauseMs = 0)
	{
		try
		{
			(string, int) tuple = RequireSelectedWritablePlayer();
			List<string> list = commands.ToList();
			if (list.Count == 0)
			{
				Log("LAN-" + feature + "-SAFE: no commands.");
				return;
			}
			int value;
			int num = (safeBatchResume.TryGetValue(feature, out value) ? Math.Clamp(value, 0, list.Count - 1) : 0);
			BeginModificationProgress(feature, list.Count);
			if (num > 0)
			{
				AdvanceModificationProgress(feature, num, list.Count);
			}
			Log($"LAN-{feature}-SAFE-START target='{tuple.Item1}' selectedClient={tuple.Item2} count={list.Count} resume={num + 1} chunk={chunkSize} cmdDelay={commandDelayMs}ms chunkPause={chunkPauseMs}ms session={sessionSize} sessionPause={sessionPauseMs}ms.");
			for (int i = num; i < list.Count; i++)
			{
				string text = list[i];
				string text2 = WrapLobbyCommand(text);
				int num2 = Encoding.ASCII.GetByteCount(text2) + 1;
				if (num2 > 512)
				{
					throw new InvalidOperationException($"Command {i + 1} exceeds recovered 512-byte limit.");
				}
				CbufSendResult cbufSendResult = debugBridge.SendVerifiedCbufCommand(bo2Process, tuple.Item2, text2);
				if (!cbufSendResult.SignatureOk || !cbufSendResult.BufferReadbackOk)
				{
					throw new InvalidOperationException($"Command {i + 1} transport verification failed.");
				}
				safeBatchResume[feature] = i + 1;
				Log($"LAN-{feature}-SAFE PASS {i + 1}/{list.Count} bytes={num2} return=0x{cbufSendResult.ReturnValue:X} cmd='{text}'.");
				AdvanceModificationProgress(feature, i + 1, list.Count);
				Application.DoEvents();
				if (i + 1 < list.Count)
				{
					Thread.Sleep(commandDelayMs);
					if ((i + 1) % Math.Max(1, chunkSize) == 0)
					{
						Log($"LAN-{feature}-SAFE CHUNK {(i + 1) / chunkSize} complete at {i + 1}/{list.Count}; cooling {chunkPauseMs}ms.");
						Thread.Sleep(chunkPauseMs);
						Application.DoEvents();
					}
					if (sessionSize > 0 && (i + 1) % sessionSize == 0)
					{
						Log($"LAN-{feature}-SAFE SESSION COMPLETE at {i + 1}/{list.Count}. HARD STOP. Progress preserved; verify/reconnect BO2/libdebug, then press the same button to resume at {i + 2}.");
						modificationStatus.Text = $"{feature}: SESSION COMPLETE {i + 1}/{list.Count} - NEXT {i + 2}";
						return;
					}
				}
			}
			safeBatchResume.Remove(feature);
			MarkModificationApplied(feature);
			Log($"LAN-{feature}-SAFE PASS target='{tuple.Item1}' selectedClient={tuple.Item2}. Save, leave/rejoin, verify.");
		}
		catch (Exception ex)
		{
			int value2;
			int num3 = (safeBatchResume.TryGetValue(feature, out value2) ? value2 : 0);
			Log($"LAN-{feature}-SAFE STOP - {ex.GetBaseException().Message}. Progress preserved at {num3}; after BO2/libdebug is healthy, press the same button to resume at {num3 + 1}.");
		}
	}

	internal static void InitializeClickSound()
	{
		UiSound.Init();
	}

	public static void PlayClickSound()
	{
		UiSound.Click();
	}

	private static void FitTabStrip(TabControl tabs, int maxWidth, int height)
	{
		if (tabs.TabCount == 0)
		{
			return;
		}
		int width = tabs.ClientSize.Width;
		if (width > 0)
		{
			int num = Math.Clamp((width - 24) / tabs.TabCount, 70, maxWidth);
			if (num != tabs.ItemSize.Width)
			{
				tabs.ItemSize = new Size(num, height);
			}
		}
	}

	private static void StyleTabStrip(TabControl tabs, int itemWidth, int itemHeight)
	{
		tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
		tabs.SizeMode = TabSizeMode.Fixed;
		tabs.ItemSize = new Size(itemWidth, itemHeight);
		tabs.Appearance = TabAppearance.FlatButtons;
		tabs.BackColor = CleanBg;
		tabs.Padding = new Point(0, 0);
		tabs.Paint -= PaintTabBackground;
		tabs.Paint += PaintTabBackground;
		tabs.DrawItem -= DrawCleanTab;
		tabs.DrawItem += DrawCleanTab;
		bool fitting = false;
		int fittedWidth = -1;
		int fittedCount = -1;
		tabs.Resize += delegate
		{
			FitSoon();
		};
		tabs.HandleCreated += delegate
		{
			FitSoon();
		};
		Fit();
		void Fit()
		{
			if (!fitting && tabs.TabCount != 0)
			{
				int num = tabs.ClientSize.Width - 8;
				if (num > 0 && (num != fittedWidth || tabs.TabCount != fittedCount))
				{
					int num2 = Math.Max(64, num / tabs.TabCount - 6);
					if (num2 != tabs.ItemSize.Width)
					{
						fitting = true;
						try
						{
							fittedWidth = num;
							fittedCount = tabs.TabCount;
							tabs.ItemSize = new Size(num2, itemHeight);
							return;
						}
						finally
						{
							fitting = false;
						}
					}
					fittedWidth = num;
					fittedCount = tabs.TabCount;
				}
			}
		}
		void FitSoon()
		{
			if (fitting || !tabs.IsHandleCreated)
			{
				return;
			}
			try
			{
				tabs.BeginInvoke(delegate
				{
					if (!fitting)
					{
						Fit();
					}
				});
			}
			catch
			{
			}
		}
	}

	private static void PaintTabBackground(object? sender, PaintEventArgs e)
	{
		if (!(sender is Control control))
		{
			return;
		}
		using SolidBrush brush = new SolidBrush(CleanBg);
		e.Graphics.FillRectangle(brush, control.ClientRectangle);
	}

	private static void DrawCleanTab(object? sender, DrawItemEventArgs e)
	{
		if (!(sender is TabControl tabControl))
		{
			return;
		}
		Rectangle bounds = e.Bounds;
		bool flag = e.Index == tabControl.SelectedIndex;
		Graphics graphics = e.Graphics;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
		using (SolidBrush brush = new SolidBrush(flag ? CleanSurface : CleanBg))
		{
			graphics.FillRectangle(brush, bounds);
		}
		if (flag)
		{
			using SolidBrush brush2 = new SolidBrush(CleanAccent);
			graphics.FillRectangle(brush2, bounds.X, bounds.Y, 3, bounds.Height);
		}
		if (e.Index < tabControl.TabCount - 1)
		{
			using Pen pen = new Pen(Color.FromArgb(64, CleanBorder), 1f);
			graphics.DrawLine(pen, bounds.Right - 1, bounds.Top + 7, bounds.Right - 1, bounds.Bottom - 7);
		}
		using SolidBrush solidBrush = new SolidBrush(Color.FromArgb(238, 246, 255));
		using SolidBrush solidBrush2 = new SolidBrush(Color.FromArgb(160, 165, 180));
		string text = tabControl.TabPages[e.Index].Text;
		graphics.DrawString(text, flag ? FontTab9B : FontTab9, flag ? solidBrush : solidBrush2, bounds, FmtCenterNW);
	}

	private static void DrawCleanWatermark(object? sender, PaintEventArgs e)
	{
		if (!(sender is TabPage tabPage))
		{
			return;
		}
		Graphics graphics = e.Graphics;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		using Font font = new Font("Segoe UI", 160f, FontStyle.Bold, GraphicsUnit.Pixel);
		using SolidBrush brush = new SolidBrush(Color.FromArgb(10, 255, 255, 255));
		SizeF sizeF = graphics.MeasureString("BO2", font);
		float x = ((float)tabPage.ClientSize.Width - sizeF.Width) / 2f;
		float y = ((float)tabPage.ClientSize.Height - sizeF.Height) / 2f;
		graphics.DrawString("BO2", font, brush, x, y);
	}

	private static void ApplyCleanTheme(Control root)
	{
		root.BackColor = CleanBg;
		root.ForeColor = CleanText;
		if (root is Form form)
		{
			form.Font = new Font("Segoe UI", 9.5f);
		}
		foreach (Control control in root.Controls)
		{
			if (!(control is CleanModeButton))
			{
				control.ForeColor = CleanText;
				if (control is Button button)
				{
					button.BackColor = CleanSurface;
					button.ForeColor = CleanText;
					button.FlatStyle = FlatStyle.Flat;
					button.FlatAppearance.BorderColor = CleanBorder;
					button.FlatAppearance.BorderSize = 1;
					button.FlatAppearance.MouseOverBackColor = CleanSurfaceHover;
					button.FlatAppearance.MouseDownBackColor = CleanSurfaceActive;
					button.Cursor = Cursors.Hand;
					button.Font = new Font("Segoe UI", 9f);
					button.Padding = new Padding(6, 3, 6, 3);
					button.UseVisualStyleBackColor = false;
				}
				else if (control is TextBox textBox)
				{
					textBox.BackColor = CleanSurface;
					textBox.ForeColor = CleanText;
					textBox.BorderStyle = BorderStyle.FixedSingle;
					textBox.Font = new Font("Segoe UI", 9f);
				}
				else if (control is ListBox listBox)
				{
					listBox.BackColor = CleanSurface;
					listBox.ForeColor = CleanText;
					listBox.BorderStyle = BorderStyle.FixedSingle;
					listBox.Font = new Font("Segoe UI", 9f);
				}
				else if (control is ComboBox comboBox)
				{
					comboBox.BackColor = CleanSurface;
					comboBox.ForeColor = CleanText;
					comboBox.FlatStyle = FlatStyle.Flat;
					comboBox.Font = new Font("Segoe UI", 9f);
				}
				else if (control is NumericUpDown numericUpDown)
				{
					numericUpDown.BackColor = CleanSurface;
					numericUpDown.ForeColor = CleanText;
					numericUpDown.BorderStyle = BorderStyle.FixedSingle;
					numericUpDown.Font = new Font("Segoe UI", 9f);
				}
				else if (control is TabPage tabPage)
				{
					tabPage.BackColor = CleanBg;
					tabPage.ForeColor = CleanText;
				}
				else if (!(control is StatusStrip) && (control is Panel || control is FlowLayoutPanel || control is TableLayoutPanel || control is SplitContainer))
				{
					control.BackColor = CleanBg;
				}
				ApplyCleanTheme(control);
			}
		}
	}

	private static void DrawBo2Tab(object? sender, DrawItemEventArgs e)
	{
		DrawCleanTab(sender, e);
	}

	private static void DrawBo2Header(object? sender, PaintEventArgs e)
	{
	}

	private static void DrawBo2Watermark(object? sender, PaintEventArgs e)
	{
		DrawCleanWatermark(sender, e);
	}

	private static void ApplyBo2Theme(Control root)
	{
		ApplyCleanTheme(root);
	}
}
internal sealed class ModeSelectionForm : Form
{
	private sealed class SelectModeButton : Button
	{
		private readonly bool _active;

		private float _glow;

		private float _targetGlow;

		private bool _hover;

		private static readonly Color Accent = Color.FromArgb(96, 165, 250);

		private static readonly Color ActiveBg = Color.FromArgb(30, 41, 59);

		private static readonly Color ActiveFg = Color.FromArgb(240, 248, 255);

		private static readonly Color IdleBg = Color.FromArgb(22, 22, 28);

		private static readonly Color IdleFg = Color.FromArgb(105, 105, 125);

		private static readonly Color LockedBg = Color.FromArgb(20, 20, 25);

		private static readonly Color LockedFg = Color.FromArgb(80, 80, 98);

		private static readonly Color Border = Color.FromArgb(48, 48, 60);

		private static readonly Color BorderBright = Color.FromArgb(72, 72, 92);

		private static readonly StringFormat FmtCtr = new StringFormat
		{
			Alignment = StringAlignment.Center,
			LineAlignment = StringAlignment.Center,
			FormatFlags = StringFormatFlags.NoWrap
		};

		public SelectModeButton(string text, bool active)
		{
			_active = active;
			_glow = (active ? 1f : 0f);
			_targetGlow = _glow;
			Text = text;
			base.FlatStyle = FlatStyle.Flat;
			base.FlatAppearance.BorderSize = 0;
			Font = new Font("Segoe UI", 13f, FontStyle.Bold);
			Cursor = Cursors.Hand;
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			if (base.Enabled)
			{
				BackColor = (active ? ActiveBg : IdleBg);
				ForeColor = (active ? ActiveFg : IdleFg);
			}
			else
			{
				BackColor = LockedBg;
				ForeColor = LockedFg;
			}
			Animate();
		}

		private void Animate()
		{
			System.Windows.Forms.Timer t = new System.Windows.Forms.Timer
			{
				Interval = 16
			};
			long last = Environment.TickCount64;
			t.Tick += delegate
			{
				long tickCount = Environment.TickCount64;
				float dt = (float)(tickCount - last) / 1000f;
				last = tickCount;
				if (!MainForm.Motion.Step(ref _glow, _targetGlow, dt, 0.06f))
				{
					_glow = _targetGlow;
					t.Stop();
					t.Dispose();
				}
				Invalidate();
			};
			t.Start();
		}

		protected override void OnMouseEnter(EventArgs e)
		{
			base.OnMouseEnter(e);
			if (base.Enabled)
			{
				_hover = true;
				_targetGlow = Math.Min(1f, _glow + 0.45f);
				if (!_active)
				{
					BackColor = Color.FromArgb(30, 30, 38);
				}
			}
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			if (base.Enabled)
			{
				MainForm.PlayClickSound();
			}
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			base.OnMouseLeave(e);
			_hover = false;
			_targetGlow = (_active ? 1f : 0f);
			if (!_active)
			{
				BackColor = IdleBg;
			}
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
			Rectangle r = new Rectangle(0, 0, base.Width - 1, base.Height - 1);
			int num = 10;
			if (_glow > 0.02f && base.Enabled)
			{
				for (int num2 = 3; num2 >= 1; num2--)
				{
					int num3 = num2 * 2;
					using Pen pen = new Pen(Color.FromArgb((int)(38f * _glow), Accent), 2f);
					graphics.DrawRectangle(pen, 1 - num3 / 2, 1 - num3 / 2, r.Width + num3, r.Height + num3);
				}
			}
			using (GraphicsPath path = Rounded(r, num))
			{
				using SolidBrush brush = new SolidBrush((!base.Enabled) ? LockedBg : (_active ? ActiveBg : (_hover ? Color.FromArgb(30, 30, 38) : IdleBg)));
				graphics.FillPath(brush, path);
				Color baseColor = ((!base.Enabled) ? Color.FromArgb(40, 40, 52) : (_active ? Accent : (_hover ? BorderBright : Border)));
				using Pen pen2 = new Pen(Color.FromArgb(_active ? 200 : (_hover ? 170 : 120), baseColor), _active ? 1.6f : 1f);
				graphics.DrawPath(pen2, path);
				if (_active)
				{
					using SolidBrush brush2 = new SolidBrush(Accent);
					graphics.FillRectangle(brush2, num, r.Bottom - 2, r.Width - num * 2, 2);
				}
			}
			using SolidBrush brush3 = new SolidBrush((!base.Enabled) ? LockedFg : (_active ? ActiveFg : (_hover ? Color.FromArgb(235, 235, 245) : IdleFg)));
			using Font font = new Font(Font.FontFamily, 15f, FontStyle.Bold);
			RectangleF layoutRectangle = new RectangleF(12f, (float)base.Height * 0.34f, Math.Max(1, base.Width - 24), 32f);
			graphics.DrawString(Text, font, brush3, layoutRectangle, FmtCtr);
			string s = ((!base.Enabled) ? "UPDATING" : (_active ? "READY TO USE" : "SELECT MODE"));
			using Font font2 = new Font(Font.FontFamily, 9f, FontStyle.Bold);
			using SolidBrush brush4 = new SolidBrush((!base.Enabled) ? Color.FromArgb(130, 130, 148) : Color.FromArgb(150, 165, 190));
			RectangleF layoutRectangle2 = new RectangleF(12f, (float)base.Height * 0.5f, Math.Max(1, base.Width - 24), 24f);
			graphics.DrawString(s, font2, brush4, layoutRectangle2, FmtCtr);
		}

		private static GraphicsPath Rounded(Rectangle r, int radius)
		{
			GraphicsPath graphicsPath = new GraphicsPath();
			int num = radius * 2;
			graphicsPath.AddArc(r.X, r.Y, num, num, 180f, 90f);
			graphicsPath.AddArc(r.Right - num, r.Y, num, num, 270f, 90f);
			graphicsPath.AddArc(r.Right - num, r.Bottom - num, num, num, 0f, 90f);
			graphicsPath.AddArc(r.X, r.Bottom - num, num, num, 90f, 90f);
			graphicsPath.CloseFigure();
			return graphicsPath;
		}
	}

	private string selected = "Multiplayer";

	private ModeSelectionForm()
	{
		Text = "BO2 TOOL @wyzyxc";
		base.Icon = Program.LoadAppIcon();
		base.Width = 760;
		base.Height = 460;
		base.StartPosition = FormStartPosition.CenterScreen;
		base.FormBorderStyle = FormBorderStyle.FixedSingle;
		base.MaximizeBox = false;
		base.MinimizeBox = false;
		BackColor = Color.FromArgb(18, 18, 22);
		ForeColor = Color.FromArgb(235, 235, 245);
		Font = new Font("Segoe UI", 9.5f);
		DoubleBuffered = true;
		Panel header = new Panel
		{
			Dock = DockStyle.Top,
			Height = 84,
			BackColor = Color.FromArgb(24, 24, 30)
		};
		header.Paint += delegate(object? s, PaintEventArgs e)
		{
			using Pen pen = new Pen(Color.FromArgb(48, 48, 60), 1f);
			e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
		};
		Label value = new Label
		{
			Text = "SELECT GAME MODE",
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleCenter,
			Font = new Font("Segoe UI", 20f, FontStyle.Bold),
			ForeColor = Color.FromArgb(235, 235, 245),
			BackColor = Color.Transparent
		};
		header.Controls.Add(value);
		base.Controls.Add(header);
		Label value2 = new Label
		{
			Text = "Choose a workspace to continue",
			Dock = DockStyle.Top,
			Height = 42,
			TextAlign = ContentAlignment.MiddleCenter,
			Font = new Font("Segoe UI", 9.5f),
			ForeColor = Color.FromArgb(100, 100, 120),
			BackColor = Color.FromArgb(18, 18, 22)
		};
		base.Controls.Add(value2);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1,
			BackColor = Color.FromArgb(18, 18, 22),
			Padding = new Padding(36, 20, 36, 28)
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		Button button = MakeButton("MULTIPLAYER", "Multiplayer", active: true);
		button.Margin = new Padding(0, 0, 12, 0);
		Button button2 = MakeButton(MainForm.StandaloneGscOptionsUpdating ? "ZOMBIES - UPDATING" : "ZOMBIES", "Zombies", active: false);
		button2.Enabled = !MainForm.StandaloneGscOptionsUpdating;
		button2.Margin = new Padding(12, 0, 0, 0);
		tableLayoutPanel.Controls.Add(button, 0, 0);
		tableLayoutPanel.Controls.Add(button2, 1, 0);
		base.Controls.Add(tableLayoutPanel);
		Label value3 = new Label
		{
			Text = "Zombies is being updated and cannot be selected right now.",
			Dock = DockStyle.Bottom,
			Height = 34,
			TextAlign = ContentAlignment.MiddleCenter,
			ForeColor = Color.FromArgb(145, 150, 166),
			BackColor = Color.FromArgb(18, 18, 22),
			Font = new Font("Segoe UI", 9f)
		};
		base.Controls.Add(value3);
	}

	private Button MakeButton(string text, string value, bool active)
	{
		SelectModeButton selectModeButton = new SelectModeButton(text, active);
		selectModeButton.Dock = DockStyle.Fill;
		selectModeButton.Click += delegate
		{
			selected = value;
			base.DialogResult = DialogResult.OK;
			Close();
		};
		return selectModeButton;
	}

	public static string ChooseMode()
	{
		using ModeSelectionForm modeSelectionForm = new ModeSelectionForm();
		return (modeSelectionForm.ShowDialog() == DialogResult.OK) ? modeSelectionForm.selected : "Multiplayer";
	}
}
internal static class Program
{
	[STAThread]
	private static void Main()
	{
		Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		ApplicationConfiguration.Initialize();
		Application.Run(new MainForm());
	}

	internal static Icon? LoadAppIcon()
	{
		try
		{
			string text = Path.Combine(AppContext.BaseDirectory, "app.ico");
			if (File.Exists(text))
			{
				return new Icon(text);
			}
			string text2 = Environment.ProcessPath ?? string.Empty;
			if (text2.Length > 0 && File.Exists(text2))
			{
				Icon icon = Icon.ExtractAssociatedIcon(text2);
				if (icon != null && icon.Width > 0)
				{
					return icon;
				}
			}
		}
		catch
		{
		}
		return null;
	}
}
internal static class UpdateService
{
	private sealed class UpdateManifest
	{
		[JsonPropertyName("version")]
		public string Version { get; set; } = "";

		[JsonPropertyName("url")]
		public string Url { get; set; } = "";

		[JsonPropertyName("sha256")]
		public string Sha256 { get; set; } = "";

		[JsonPropertyName("signature")]
		public string Signature { get; set; } = "";
	}

	internal sealed record UpdateCheck(bool Configured, bool Available, string CurrentVersion, string LatestVersion, Uri? DownloadUri, string Sha256)
	{
		internal static UpdateCheck NotConfigured(string currentVersion)
		{
			return new UpdateCheck(Configured: false, Available: false, currentVersion, currentVersion, null, "");
		}
	}

	private const string PublicKeyBase64 = "BgIAAACkAABSU0ExAAwAAAEAAQB5V8bHAAEHSgFtrrggNzY80kfpxy21C5CgrJHC8+leScLraC8V70jf3aFOxnWFsVdA58kSuQIYJs09Gtt0XGlDmwlamgfBbfnBKWkNQmH6XqQUjpcHfqAj8trm8ONWuc0QCPpiN8ztiS9VRqnNsg2bh0+ZShTJpzSudDd8EFhStcJFbbSjvR0sLoVLuowCHd436YTDp3V5/C/UUdc8/dnAPa8t1ZVJa0PzHiEe6NbV2UieXq3v8rRIGJ1VxTTgKBB534DR28EeYa497CaePYSgacFKur9bOs8sKBaDkCE24K8vIkdAT+Eoqw99FifQbIfwVK9mQYwgBJGLGM6GuSJjWQT/SwL7ENHeIkqLZHg3LQX8mYLAsS5e15JhyRemBBQsW2NS/GdKTJQPMJ+s75AmXXhE+W0DXRXN6X+GuFOjJWMruN9mlDD+0W+MkWivZvWBZNqaSK4qOif45MGimpCgsDW9oITywOD7TtWbXe5rMSFymx8yxko6lFOh0cTMJMM=";

	private static readonly HttpClient Http = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(20.0)
	};

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true
	};

	private const long MaximumDownloadBytes = 262144000L;

	internal static string CurrentVersion
	{
		get
		{
			Version version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
			return $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
		}
	}

	internal static bool IsConfigured
	{
		get
		{
			Uri manifestUri;
			return TryGetManifestUri(out manifestUri);
		}
	}

	internal static async Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!TryGetManifestUri(out Uri manifestUri))
		{
			return UpdateCheck.NotConfigured(CurrentVersion);
		}
		using HttpResponseMessage response = await Http.GetAsync(manifestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		response.EnsureSuccessStatusCode();
		if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
		{
			throw new InvalidDataException("The update manifest did not remain on HTTPS.");
		}
		UpdateCheck result3;
		await using (Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken))
		{
			UpdateManifest updateManifest = (await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, JsonOptions, cancellationToken)) ?? throw new InvalidDataException("The update manifest is empty or invalid.");
			if (!Version.TryParse(updateManifest.Version, out Version result))
			{
				throw new InvalidDataException("The update manifest contains an invalid version.");
			}
			if (!Uri.TryCreate(updateManifest.Url, UriKind.Absolute, out Uri result2) || result2.Scheme != Uri.UriSchemeHttps)
			{
				throw new InvalidDataException("The update download URL must use HTTPS.");
			}
			if (updateManifest.Sha256.Length != 64 || !updateManifest.Sha256.All(Uri.IsHexDigit))
			{
				throw new InvalidDataException("The update manifest contains an invalid SHA-256 hash.");
			}
			if (updateManifest.Signature.Length > 2048)
			{
				throw new InvalidDataException("The update signature is too large.");
			}
			byte[] signature;
			try
			{
				signature = Convert.FromBase64String(updateManifest.Signature);
			}
			catch (FormatException innerException)
			{
				throw new InvalidDataException("The update signature is invalid.", innerException);
			}
			string s = $"{updateManifest.Version}\n{updateManifest.Url}\n{updateManifest.Sha256.ToLowerInvariant()}";
			using RSA rSA = new RSACryptoServiceProvider();
			((RSACryptoServiceProvider)rSA).ImportCspBlob(Convert.FromBase64String("BgIAAACkAABSU0ExAAwAAAEAAQB5V8bHAAEHSgFtrrggNzY80kfpxy21C5CgrJHC8+leScLraC8V70jf3aFOxnWFsVdA58kSuQIYJs09Gtt0XGlDmwlamgfBbfnBKWkNQmH6XqQUjpcHfqAj8trm8ONWuc0QCPpiN8ztiS9VRqnNsg2bh0+ZShTJpzSudDd8EFhStcJFbbSjvR0sLoVLuowCHd436YTDp3V5/C/UUdc8/dnAPa8t1ZVJa0PzHiEe6NbV2UieXq3v8rRIGJ1VxTTgKBB534DR28EeYa497CaePYSgacFKur9bOs8sKBaDkCE24K8vIkdAT+Eoqw99FifQbIfwVK9mQYwgBJGLGM6GuSJjWQT/SwL7ENHeIkqLZHg3LQX8mYLAsS5e15JhyRemBBQsW2NS/GdKTJQPMJ+s75AmXXhE+W0DXRXN6X+GuFOjJWMruN9mlDD+0W+MkWivZvWBZNqaSK4qOif45MGimpCgsDW9oITywOD7TtWbXe5rMSFymx8yxko6lFOh0cTMJMM="));
			if (!rSA.VerifyData(Encoding.UTF8.GetBytes(s), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
			{
				throw new InvalidDataException("The update signature could not be verified. The file was not trusted.");
			}
			Version version = Version.Parse(CurrentVersion);
			result3 = ((result > version) ? new UpdateCheck(Configured: true, Available: true, CurrentVersion, updateManifest.Version, result2, updateManifest.Sha256.ToLowerInvariant()) : new UpdateCheck(Configured: true, Available: false, CurrentVersion, updateManifest.Version, result2, updateManifest.Sha256.ToLowerInvariant()));
		}
		return result3;
	}

	internal static async Task<string> DownloadAndVerifyAsync(UpdateCheck update, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!update.Configured || !update.Available || (object)update.DownloadUri == null)
		{
			throw new InvalidOperationException("There is no verified update to download.");
		}
		using HttpResponseMessage response = await Http.GetAsync(update.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		response.EnsureSuccessStatusCode();
		if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
		{
			throw new InvalidDataException("The update download did not remain on HTTPS.");
		}
		long? contentLength = response.Content.Headers.ContentLength;
		if (contentLength.HasValue)
		{
			long valueOrDefault = contentLength.GetValueOrDefault();
			if (valueOrDefault <= 0 || valueOrDefault > 262144000)
			{
				throw new InvalidDataException("The update file size is outside the allowed range.");
			}
		}
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BO2RTM", "updates");
		Directory.CreateDirectory(text);
		string stagedPath = Path.Combine(text, $"BO2RTM-{Guid.NewGuid():N}.exe");
		try
		{
			await using (Stream input = await response.Content.ReadAsStreamAsync(cancellationToken))
			{
				await using FileStream output = new FileStream(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
				byte[] buffer = new byte[81920];
				long total = 0L;
				int num;
				while ((num = await input.ReadAsync(buffer, cancellationToken)) > 0)
				{
					total += num;
					if (total > 262144000)
					{
						throw new InvalidDataException("The update file exceeded the allowed size.");
					}
					await output.WriteAsync(buffer.AsMemory(0, num), cancellationToken);
				}
			}
			string result;
			await using (FileStream file = File.OpenRead(stagedPath))
			{
				if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken)).ToLowerInvariant()), Convert.FromHexString(update.Sha256)))
				{
					throw new InvalidDataException("The downloaded EXE does not match the signed SHA-256 hash.");
				}
				result = stagedPath;
			}
			return result;
		}
		catch
		{
			try
			{
				File.Delete(stagedPath);
			}
			catch
			{
			}
			throw;
		}
	}

	internal static void StartConfirmedInstall(string stagedPath, string expectedSha256)
	{
		string processPath = Environment.ProcessPath;
		if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
		{
			throw new InvalidOperationException("The running EXE path could not be located.");
		}
		processPath = Path.GetFullPath(processPath);
		string path = Path.Combine(Path.GetDirectoryName(processPath), $".bo2-update-{Guid.NewGuid():N}.tmp");
		try
		{
			using (File.Create(path))
			{
			}
		}
		catch (Exception innerException)
		{
			throw new InvalidOperationException("This folder does not allow the app to update itself. Move the EXE to a writable folder and try again.", innerException);
		}
		finally
		{
			try
			{
				File.Delete(path);
			}
			catch
			{
			}
		}
		int processId = Environment.ProcessId;
		string s = JsonSerializer.Serialize(new
		{
			targetPath = processPath,
			stagedPath = stagedPath,
			expectedSha256 = expectedSha256,
			pid = processId
		});
		string text = Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
		string s2 = "$cfg = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + text + "')) | ConvertFrom-Json\nWrite-Host 'Applying the update you approved. Please wait...'\nwhile (Get-Process -Id $cfg.pid -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 500 }\nStart-Sleep -Milliseconds 500\n$actual = (Get-FileHash -LiteralPath $cfg.stagedPath -Algorithm SHA256).Hash.ToLowerInvariant()\nif ($actual -ne $cfg.expectedSha256) { Write-Error 'The staged update hash changed.'; Read-Host 'Press Enter to close'; exit 1 }\n$backup = \"$($cfg.targetPath).update-backup\"\ntry {\n    Copy-Item -LiteralPath $cfg.targetPath -Destination $backup -Force\n    Move-Item -LiteralPath $cfg.stagedPath -Destination $cfg.targetPath -Force\n    $installed = (Get-FileHash -LiteralPath $cfg.targetPath -Algorithm SHA256).Hash.ToLowerInvariant()\n    if ($installed -ne $cfg.expectedSha256) { throw 'Installed file hash check failed.' }\n    Remove-Item -LiteralPath $backup -Force\n    Start-Process -FilePath $cfg.targetPath\n    Write-Host 'Update installed.'\n} catch {\n    if (Test-Path -LiteralPath $backup) { Copy-Item -LiteralPath $backup -Destination $cfg.targetPath -Force }\n    Write-Error $_\n    Read-Host 'The previous EXE was restored. Press Enter to close'\n    exit 1\n}";
		string text2 = Convert.ToBase64String(Encoding.Unicode.GetBytes(s2));
		if (Process.Start(new ProcessStartInfo
		{
			FileName = "powershell.exe",
			Arguments = "-NoProfile -EncodedCommand " + text2,
			UseShellExecute = false,
			CreateNoWindow = false,
			WindowStyle = ProcessWindowStyle.Normal
		}) == null)
		{
			throw new InvalidOperationException("Could not start the visible update installer.");
		}
	}

	private static bool TryGetManifestUri(out Uri manifestUri)
	{
		if (Uri.TryCreate(Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault((AssemblyMetadataAttribute attribute) => attribute.Key == "BO2RTM.UpdateManifestUrl")?.Value, UriKind.Absolute, out Uri result) && result.Scheme == Uri.UriSchemeHttps && !result.Host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase))
		{
			manifestUri = result;
			return true;
		}
		manifestUri = null;
		return false;
	}
}
internal sealed class WelcomeForm : Form
{
	private string? selectedConsole;

	private readonly TextBox ipBox = new TextBox
	{
		Text = "192.168.1.100",
		Width = 260
	};

	private readonly Label statusLabel = new Label
	{
		Text = "SELECT CONSOLE THEN CONNECT",
		AutoSize = true
	};

	private readonly Label consoleLabel = new Label
	{
		Text = "CONSOLE:",
		AutoSize = true
	};

	private readonly Button connectBtn = new Button
	{
		Text = "CONNECT",
		AutoSize = true
	};

	private readonly System.Windows.Forms.Timer pulseTimer = new System.Windows.Forms.Timer
	{
		Interval = 500
	};

	private bool pulseState;

	private readonly Label notifLabel = new Label
	{
		Text = "",
		AutoSize = true
	};

	private static readonly Color NeonCyan = Color.FromArgb(0, 255, 255);

	private static readonly Color NeonBlue = Color.FromArgb(0, 170, 255);

	private static readonly Color NeonPurple = Color.FromArgb(170, 0, 255);

	private static readonly Color NeonGreen = Color.FromArgb(0, 255, 100);

	private static readonly Color NeonOrange = Color.FromArgb(255, 100, 0);

	private static readonly Color DarkBg = Color.FromArgb(5, 5, 15);

	private static Font connFont = new Font("Segoe UI", 8f);

	public string? SelectedConsole => selectedConsole;

	public string IPAddress => ipBox.Text.Trim();

	public bool IsConnected { get; private set; }

	public WelcomeForm()
	{
		Text = "BO2 RTM v6.32";
		base.Width = 720;
		base.Height = 520;
		base.StartPosition = FormStartPosition.CenterScreen;
		base.FormBorderStyle = FormBorderStyle.None;
		BackColor = DarkBg;
		DoubleBuffered = true;
		base.KeyPreview = true;
		try
		{
			string path = Path.Combine(Application.StartupPath, "bo2_ip.txt");
			if (File.Exists(path))
			{
				string text = File.ReadAllText(path).Trim();
				if (!string.IsNullOrWhiteSpace(text))
				{
					ipBox.Text = text;
				}
			}
		}
		catch
		{
		}
		pulseTimer.Tick += delegate
		{
			pulseState = !pulseState;
			Invalidate();
		};
		pulseTimer.Start();
		base.Paint += DrawFuturisticBg;
		SetupUI();
	}

	private void SetupUI()
	{
		Panel panel = new Panel
		{
			Dock = DockStyle.Top,
			Height = 100,
			BackColor = Color.Transparent
		};
		panel.Paint += delegate(object? s, PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using Font font = new Font("Segoe UI", 28f, FontStyle.Bold);
			using (new SolidBrush(Color.FromArgb(60, 0, 255, 255)))
			{
				using SolidBrush brush = new SolidBrush(NeonCyan);
				string text = "BO2 RTM v6.32";
				SizeF sizeF = graphics.MeasureString(text, font);
				float num = ((float)base.ClientSize.Width - sizeF.Width) / 2f;
				float num2 = 15f;
				for (int num3 = 6; num3 >= 1; num3--)
				{
					using SolidBrush brush2 = new SolidBrush(Color.FromArgb(30 / num3, 0, 255, 255));
					graphics.DrawString(text, font, brush2, num + (float)num3, num2 + (float)num3);
				}
				graphics.DrawString(text, font, brush, num, num2);
				using Font font2 = new Font("Segoe UI", 11f, FontStyle.Regular);
				using SolidBrush brush3 = new SolidBrush(Color.FromArgb(150, 0, 200, 255));
				string text2 = "BLACK OPS II — TOOL RECOVERY MANAGER";
				SizeF sizeF2 = graphics.MeasureString(text2, font2);
				graphics.DrawString(text2, font2, brush3, ((float)base.ClientSize.Width - sizeF2.Width) / 2f, num2 + sizeF.Height + 5f);
				int num4 = DateTime.Now.Millisecond % 200;
				using Pen pen = new Pen(Color.FromArgb(80, 0, 255, 255), 1f);
				graphics.DrawLine(pen, 0, num4 + 50, base.ClientSize.Width, num4 + 50);
			}
		};
		base.Controls.Add(panel);
		Panel consolePanel = new Panel
		{
			Width = 600,
			Height = 80,
			Location = new Point(60, 120),
			BackColor = Color.Transparent
		};
		consolePanel.Paint += delegate(object? s, PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			using Pen pen = new Pen(pulseState ? NeonCyan : NeonBlue, 2f);
			graphics.DrawRoundedRectangle(pen, 0, 0, consolePanel.Width - 1, consolePanel.Height - 1, 8);
			using SolidBrush brush = new SolidBrush(Color.FromArgb(20, 0, 30, 50));
			graphics.FillRoundedRectangle(brush, 0, 0, consolePanel.Width - 1, consolePanel.Height - 1, 8);
		};
		consoleLabel.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
		consoleLabel.ForeColor = NeonCyan;
		consoleLabel.Location = new Point(20, 10);
		Button ps4Btn = MakeFuturisticButton("PS4", Color.FromArgb(0, 100, 200));
		ps4Btn.Location = new Point(180, 15);
		ps4Btn.Click += delegate
		{
			SelectConsole("PS4", ps4Btn);
		};
		Button ps5Btn = MakeFuturisticButton("PS5", Color.FromArgb(150, 0, 200));
		ps5Btn.Location = new Point(380, 15);
		ps5Btn.Click += delegate
		{
			SelectConsole("PS5", ps5Btn);
		};
		consolePanel.Controls.Add(consoleLabel);
		consolePanel.Controls.Add(ps4Btn);
		consolePanel.Controls.Add(ps5Btn);
		base.Controls.Add(consolePanel);
		Panel ipPanel = new Panel
		{
			Width = 500,
			Height = 70,
			Location = new Point(110, 220),
			BackColor = Color.Transparent
		};
		ipPanel.Paint += delegate(object? s, PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			using Pen pen = new Pen(NeonBlue, 1f);
			graphics.DrawRoundedRectangle(pen, 0, 0, ipPanel.Width - 1, ipPanel.Height - 1, 6);
			using SolidBrush brush = new SolidBrush(Color.FromArgb(15, 0, 20, 40));
			graphics.FillRoundedRectangle(brush, 0, 0, ipPanel.Width - 1, ipPanel.Height - 1, 6);
		};
		Label value = new Label
		{
			Text = "PS4 IP:",
			Font = new Font("Segoe UI", 11f, FontStyle.Bold),
			ForeColor = NeonCyan,
			Location = new Point(15, 5),
			AutoSize = true
		};
		ipBox.Font = new Font("Consolas", 13f);
		ipBox.ForeColor = NeonCyan;
		ipBox.BackColor = Color.FromArgb(10, 10, 25);
		ipBox.BorderStyle = BorderStyle.None;
		ipBox.Location = new Point(15, 30);
		ipBox.KeyDown += delegate(object? _, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Return)
			{
				TryConnect();
			}
		};
		ipPanel.Controls.Add(value);
		ipPanel.Controls.Add(ipBox);
		base.Controls.Add(ipPanel);
		connectBtn.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
		connectBtn.Location = new Point((base.ClientSize.Width - 200) / 2, 320);
		connectBtn.Size = new Size(200, 50);
		connectBtn.FlatStyle = FlatStyle.Flat;
		connectBtn.FlatAppearance.BorderSize = 0;
		connectBtn.BackColor = Color.FromArgb(0, 80, 180);
		connectBtn.ForeColor = Color.White;
		connectBtn.Cursor = Cursors.Hand;
		connectBtn.Click += delegate
		{
			TryConnect();
		};
		connectBtn.Paint += delegate(object? s, PaintEventArgs e)
		{
			if (!(s is Button button))
			{
				return;
			}
			Graphics graphics = e.Graphics;
			using Pen pen = new Pen(NeonCyan, 2f);
			graphics.DrawRoundedRectangle(pen, 0, 0, button.Width - 1, button.Height - 1, 8);
			StringFormat format = new StringFormat
			{
				Alignment = StringAlignment.Center,
				LineAlignment = StringAlignment.Center
			};
			graphics.DrawString(button.Text, button.Font, new SolidBrush(button.ForeColor), button.DisplayRectangle, format);
		};
		connectBtn.MouseEnter += delegate
		{
			connectBtn.BackColor = Color.FromArgb(0, 120, 255);
		};
		connectBtn.MouseLeave += delegate
		{
			connectBtn.BackColor = Color.FromArgb(0, 80, 180);
		};
		base.Controls.Add(connectBtn);
		statusLabel.Font = new Font("Segoe UI", 10f);
		statusLabel.ForeColor = Color.FromArgb(200, 200, 200);
		statusLabel.Location = new Point(110, 390);
		base.Controls.Add(statusLabel);
		notifLabel.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
		notifLabel.Location = new Point((base.ClientSize.Width - 300) / 2, 420);
		notifLabel.Size = new Size(300, 25);
		notifLabel.TextAlign = ContentAlignment.MiddleCenter;
		base.Controls.Add(notifLabel);
		Panel panel2 = new Panel
		{
			Width = 600,
			Height = 40,
			Location = new Point(60, 460),
			BackColor = Color.Transparent
		};
		panel2.Paint += delegate(object? s, PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			using SolidBrush brush = new SolidBrush((selectedConsole == "PS4") ? NeonGreen : Color.FromArgb(60, 60, 60));
			graphics.FillEllipse(brush, 100, 5, 12, 12);
			using SolidBrush brush2 = new SolidBrush(Color.FromArgb(150, 150, 150));
			using Font font = new Font("Segoe UI", 8f);
			graphics.DrawString("PS4", font, brush2, 120f, 3f);
			using SolidBrush brush3 = new SolidBrush((selectedConsole == "PS5") ? NeonGreen : Color.FromArgb(60, 60, 60));
			graphics.FillEllipse(brush3, 300, 5, 12, 12);
			using SolidBrush brush4 = new SolidBrush(Color.FromArgb(150, 150, 150));
			graphics.DrawString("PS5", font, brush4, 320f, 3f);
			using SolidBrush brush5 = new SolidBrush(IsConnected ? NeonGreen : Color.FromArgb(60, 60, 60));
			graphics.FillEllipse(brush5, 500, 5, 12, 12);
			using SolidBrush brush6 = new SolidBrush(Color.FromArgb(150, 150, 150));
			graphics.DrawString("CONNECTED", connFont, brush6, 520f, 3f);
		};
		base.Controls.Add(panel2);
	}

	private Button MakeFuturisticButton(string text, Color accentColor)
	{
		Button button = new Button();
		button.Text = text;
		button.AutoSize = true;
		button.Padding = new Padding(20, 8, 20, 8);
		button.FlatStyle = FlatStyle.Flat;
		button.FlatAppearance.BorderSize = 0;
		button.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
		button.Cursor = Cursors.Hand;
		Button btn = button;
		btn.BackColor = Color.FromArgb(15, 15, 30);
		btn.ForeColor = accentColor;
		btn.FlatAppearance.BorderColor = accentColor;
		btn.FlatAppearance.BorderSize = 1;
		btn.Paint += delegate(object? s, PaintEventArgs e)
		{
			if (!(s is Button button2))
			{
				return;
			}
			Graphics graphics = e.Graphics;
			using Pen pen = new Pen(button2.FlatAppearance.BorderColor, 1f);
			graphics.DrawRoundedRectangle(pen, 0, 0, button2.Width - 1, button2.Height - 1, 5);
			StringFormat format = new StringFormat
			{
				Alignment = StringAlignment.Center,
				LineAlignment = StringAlignment.Center
			};
			graphics.DrawString(button2.Text, button2.Font, new SolidBrush(button2.ForeColor), button2.DisplayRectangle, format);
		};
		btn.MouseEnter += delegate
		{
			btn.BackColor = Color.FromArgb(25, 25, 50);
		};
		btn.MouseLeave += delegate
		{
			btn.BackColor = Color.FromArgb(15, 15, 30);
		};
		return btn;
	}

	private void SelectConsole(string console, Button btn)
	{
		selectedConsole = console;
		statusLabel.Text = console + " SELECTED — Enter IP and press CONNECT";
		statusLabel.ForeColor = ((console == "PS4") ? NeonCyan : NeonPurple);
		notifLabel.Text = "";
		Invalidate();
	}

	private async void TryConnect()
	{
		if (string.IsNullOrWhiteSpace(selectedConsole))
		{
			statusLabel.Text = "⚠ SELECT A CONSOLE FIRST";
			statusLabel.ForeColor = NeonOrange;
			return;
		}
		if (string.IsNullOrWhiteSpace(ipBox.Text.Trim()) || !ipBox.Text.Trim().Contains("."))
		{
			statusLabel.Text = "⚠ INVALID IP ADDRESS";
			statusLabel.ForeColor = NeonOrange;
			return;
		}
		connectBtn.Enabled = false;
		connectBtn.Text = "CONNECTING...";
		statusLabel.Text = "Probing debug service port 744...";
		statusLabel.ForeColor = Color.FromArgb(255, 200, 0);
		try
		{
			using CancellationTokenSource cts = new CancellationTokenSource(3000);
			using TcpClient client = new TcpClient();
			await client.ConnectAsync(ipBox.Text.Trim(), 744, cts.Token);
			client.Close();
			try
			{
				File.WriteAllText(Path.Combine(Application.StartupPath, "bo2_ip.txt"), ipBox.Text.Trim());
			}
			catch
			{
			}
			IsConnected = true;
			connectBtn.Text = "✓ CONNECTED";
			connectBtn.BackColor = Color.FromArgb(0, 150, 60);
			statusLabel.Text = "✓ " + selectedConsole + " CONNECTION ESTABLISHED at " + ipBox.Text.Trim();
			statusLabel.ForeColor = NeonGreen;
			notifLabel.Text = "SENDING PS4 NOTIFICATION...";
			notifLabel.ForeColor = Color.FromArgb(255, 200, 0);
			pulseTimer.Stop();
			string consoleIp = ipBox.Text.Trim();
			bool flag = await Task.Run(() => SendConsoleNotification(consoleIp));
			notifLabel.Text = (flag ? "\ud83d\udd14 PS4 NOTIFICATION SENT" : "CONSOLE CONNECTED · NOTIFICATION FAILED");
			notifLabel.ForeColor = (flag ? NeonGreen : NeonOrange);
			await Task.Delay(1500);
			OpenMainForm();
		}
		catch
		{
			connectBtn.Enabled = true;
			connectBtn.Text = "CONNECT";
			connectBtn.BackColor = Color.FromArgb(0, 80, 180);
			statusLabel.Text = "⚠ CONNECTION FAILED — Check IP and payload status";
			statusLabel.ForeColor = NeonOrange;
			notifLabel.Text = "OFFLINE — VERIFY PAYLOAD AND IP";
			notifLabel.ForeColor = NeonOrange;
			pulseTimer.Start();
		}
	}

	private bool SendConsoleNotification(string consoleIp)
	{
		DebugBridge debugBridge = null;
		try
		{
			debugBridge = new DebugBridge(consoleIp);
			debugBridge.Connect();
			debugBridge.NotifyConsole(222, "@wyzyxc BO2 RTM connected");
			LogQuiet("PS4 SYSTEM NOTIFICATION acknowledged by the debug payload.");
			return true;
		}
		catch (Exception ex)
		{
			LogQuiet("PS4 SYSTEM NOTIFICATION failed: " + ex.GetBaseException().Message);
			return false;
		}
		finally
		{
			try
			{
				debugBridge?.Disconnect();
			}
			catch
			{
			}
		}
	}

	private void OpenMainForm()
	{
		MainForm mainForm = new MainForm();
		mainForm.FormClosed += delegate
		{
			Close();
		};
		mainForm.Show();
		Hide();
	}

	private void DrawFuturisticBg(object? sender, PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		using LinearGradientBrush brush = new LinearGradientBrush(base.ClientRectangle, Color.FromArgb(5, 5, 15), Color.FromArgb(10, 10, 30), 90f);
		graphics.FillRectangle(brush, base.ClientRectangle);
		using Pen pen = new Pen(Color.FromArgb(20, 0, 60, 80), 1f);
		for (int i = 0; i < base.ClientSize.Width; i += 40)
		{
			graphics.DrawLine(pen, i, 0, i, base.ClientSize.Height);
		}
		for (int j = 0; j < base.ClientSize.Height; j += 40)
		{
			graphics.DrawLine(pen, 0, j, base.ClientSize.Width, j);
		}
		int num = 60;
		using Pen pen2 = new Pen(NeonCyan, 3f);
		graphics.DrawLine(pen2, 0, num, 0, 0);
		graphics.DrawLine(pen2, 0, 0, num, 0);
		graphics.DrawLine(pen2, base.ClientSize.Width - num, 0, base.ClientSize.Width, 0);
		graphics.DrawLine(pen2, base.ClientSize.Width, 0, base.ClientSize.Width, num);
		graphics.DrawLine(pen2, 0, base.ClientSize.Height - num, 0, base.ClientSize.Height);
		graphics.DrawLine(pen2, 0, base.ClientSize.Height, num, base.ClientSize.Height);
		graphics.DrawLine(pen2, base.ClientSize.Width - num, base.ClientSize.Height, base.ClientSize.Width, base.ClientSize.Height);
		graphics.DrawLine(pen2, base.ClientSize.Width, base.ClientSize.Height - num, base.ClientSize.Width, base.ClientSize.Height - num);
		int num2 = (int)((double)DateTime.Now.Millisecond * 0.5 % (double)base.ClientSize.Height);
		using Pen pen3 = new Pen(Color.FromArgb(40, 0, 255, 255), 1f);
		graphics.DrawLine(pen3, 0, num2, base.ClientSize.Width, num2);
		using SolidBrush brush2 = new SolidBrush(Color.FromArgb(20, 0, 30, 40));
		graphics.FillRectangle(brush2, 0, base.ClientSize.Height - 45, base.ClientSize.Width, 45);
		using Pen pen4 = new Pen(NeonCyan, 1f);
		graphics.DrawRectangle(pen4, 0, base.ClientSize.Height - 45, base.ClientSize.Width - 1, 45);
		using Font font = new Font("Segoe UI", 8f);
		using SolidBrush brush3 = new SolidBrush(Color.FromArgb(150, 150, 150));
		graphics.DrawString("BO2 RTM v6.32 — SELECT CONSOLE & CONNECT — All features require compatible debug payload", font, brush3, 10f, base.ClientSize.Height - 30);
	}

	private void LogQuiet(string s)
	{
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		pulseTimer.Stop();
		base.OnFormClosing(e);
	}
}
internal static class GraphicsExtensions
{
	public static void DrawRoundedRectangle(this Graphics g, Pen pen, int x, int y, int width, int height, int radius)
	{
		using GraphicsPath path = GetRoundedPath(x, y, width, height, radius);
		g.DrawPath(pen, path);
	}

	public static void FillRoundedRectangle(this Graphics g, Brush brush, int x, int y, int width, int height, int radius)
	{
		using GraphicsPath path = GetRoundedPath(x, y, width, height, radius);
		g.FillPath(brush, path);
	}

	private static GraphicsPath GetRoundedPath(int x, int y, int width, int height, int radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		int num = radius * 2;
		graphicsPath.AddArc(x, y, num, num, 180f, 90f);
		graphicsPath.AddArc(x + width - num, y, num, num, 270f, 90f);
		graphicsPath.AddArc(x + width - num, y + height - num, num, num, 0f, 90f);
		graphicsPath.AddArc(x, y + height - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}
}
