using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Barotrauma.Networking;
using MoonSharp.Interpreter;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;
using Barotrauma.Items.Components;
using System.Diagnostics;

namespace Barotrauma
{
	partial class LuaSetup
	{

		public Script lua;
		public LuaHook hook;
		public LuaGame game;

		public void HandleLuaException(Exception ex)
		{
			if(ex is InterpreterException)
			{
				PrintMessage(((InterpreterException)ex).DecoratedMessage);
			}
			else
			{
				PrintMessage(ex.ToString());
			}
		}

		public void PrintMessage(object message)
		{
			Console.WriteLine(message.ToString());
			if (GameMain.Server != null)
			{
				foreach (var c in GameMain.Server.ConnectedClients)
				{
					GameMain.Server.SendDirectChatMessage(message.ToString(), c, ChatMessageType.Console);
				}

				GameServer.Log("[LUA] " + message.ToString(), ServerLog.MessageType.ServerMessage);
			}
		}

		public void PrintMessageNoLog(object message)
		{
			Console.WriteLine(message.ToString());
		}

		public DynValue DoString(string code, Table globalContext = null, string codeStringFriendly = null)
		{
			try
			{
				return lua.DoString(code, globalContext, codeStringFriendly);
			}
			catch (Exception e)
			{
				HandleLuaException(e);
			}

			return null;
		}

		public DynValue DoFile(string file, Table globalContext=null, string codeStringFriendly = null)
		{
			try
			{
				return lua.DoFile(file, globalContext, codeStringFriendly);
				
			}
			catch (Exception e)
			{
				HandleLuaException(e);
			}

			return null;
		}


		public DynValue LoadString(string file, Table globalContext = null, string codeStringFriendly = null)
		{
			try
			{
				return lua.LoadString(file, globalContext, codeStringFriendly);

			}
			catch (Exception e)
			{
				HandleLuaException(e);
			}

			return null;
		}

		public DynValue LoadFile(string file, Table globalContext = null, string codeStringFriendly = null)
		{
			try
			{
				return lua.LoadFile(file, globalContext, codeStringFriendly);

			}
			catch (Exception e)
			{
				HandleLuaException(e);
			}

			return null;
		}

		public DynValue Require(string modname, Table globalContext)
		{
			try
			{
				return lua.Call(lua.RequireModule(modname, globalContext));

			}
			catch (Exception e)
			{
				HandleLuaException(e);
			}

			return null;
		}

		public static DynValue CreateUserDataSafe(object o)
		{
			if(o == null)
				return DynValue.Nil;

			return UserData.Create(o);
			
		}


		public LuaSetup()
		{
			PrintMessage("Lua!");

			LuaScriptLoader luaScriptLoader = new LuaScriptLoader(this);
			luaScriptLoader.ModulePaths = new string[] { };

			LuaCustomConverters.RegisterAll();
			
			UserData.RegisterType<TraitorMessageType>();
			UserData.RegisterType<JobPrefab>();
			UserData.RegisterType<CharacterInfo>();
			UserData.RegisterType<Rectangle>();
			UserData.RegisterType<Point>();
			UserData.RegisterType<Level.InterestingPosition>();
			UserData.RegisterType<Level.PositionType>();
			UserData.RegisterType<Level>();
			UserData.RegisterType<Items.Components.Steering>();
			UserData.RegisterType<ServerLog.MessageType>();
			UserData.RegisterType<SpawnType>();
			UserData.RegisterType<ChatMessageType>();
			UserData.RegisterType<WayPoint>();
			UserData.RegisterType<Character>();
			UserData.RegisterType<Item>();
			UserData.RegisterType<Submarine>();
			UserData.RegisterType<Client>();
			UserData.RegisterType<LuaPlayer>();
			UserData.RegisterType<LuaHook>();
			UserData.RegisterType<LuaGame>();
			UserData.RegisterType<LuaRandom>();
			UserData.RegisterType<LuaTimer>();
			UserData.RegisterType<LuaFile>();
			UserData.RegisterType<LuaNetworking>();
			UserData.RegisterType<Vector2>();
			UserData.RegisterType<Vector3>();
			UserData.RegisterType<Vector4>();
			UserData.RegisterType<CauseOfDeathType>();
			UserData.RegisterType<AfflictionPrefab>();
			UserData.RegisterType<Affliction>();
			UserData.RegisterType<CharacterHealth>();
			UserData.RegisterType<AnimController>();
			UserData.RegisterType<Limb>();
			UserData.RegisterType<Ragdoll>();
			UserData.RegisterType<ChatMessage>();
			UserData.RegisterType<CharacterHealth.LimbHealth>();
			UserData.RegisterType<InputType>();
			UserData.RegisterType<AttackResult>();
			UserData.RegisterType<Entity>();
			UserData.RegisterType<MapEntity>();
			UserData.RegisterType<MapEntityPrefab>();
			UserData.RegisterType<CauseOfDeath>();
			UserData.RegisterType<CharacterTeamType>();
			UserData.RegisterType<Signal>();
			UserData.RegisterType<Connection>();
			UserData.RegisterType<ItemComponent>();
			UserData.RegisterType<WifiComponent>();
			UserData.RegisterType<LightComponent>();
			UserData.RegisterType<CustomInterface>();
			UserData.RegisterType<Inventory>();
			UserData.RegisterType<CharacterInventory>();
			UserData.RegisterType<Hull>();
			UserData.RegisterType<Gap>();
			UserData.RegisterType<PhysicsBody>();
			UserData.RegisterType<SubmarineBody>();
			UserData.RegisterType<InvSlotType>();
			UserData.RegisterType<ItemPrefab>();
			UserData.RegisterType<SerializableProperty>();
			UserData.RegisterType<StatusEffect>();
			UserData.RegisterType<CustomInterface.CustomInterfaceElement>();
			UserData.RegisterType<FireSource>();
			UserData.RegisterType<Fabricator>();
			UserData.RegisterType<Pair<JobPrefab, int>>();
			UserData.RegisterType<ContentPackage>();
			UserData.RegisterType<SubmarineInfo>();
			UserData.RegisterType<SubmarineBody>();

			lua = new Script(CoreModules.Preset_SoftSandbox);

			lua.Options.DebugPrint = PrintMessage;

			lua.Options.ScriptLoader = luaScriptLoader;
			
			hook = new LuaHook(this);
			game = new LuaGame(this);

			lua.Globals["printNoLog"] = (Action<object>)PrintMessageNoLog;

			lua.Globals["dofile"] = (Func<string, Table, string, DynValue>)DoFile;
			lua.Globals["loadfile"] = (Func<string, Table, string, DynValue>)LoadFile;
			lua.Globals["require"] = (Func<string, Table, DynValue>)Require;

			lua.Globals["dostring"] = (Func<string, Table, string, DynValue>)DoString;
			lua.Globals["load"] = (Func<string, Table, string, DynValue>)LoadString;

			lua.Globals["Player"] = new LuaPlayer();
			lua.Globals["Game"] = game;
			lua.Globals["Hook"] = hook;
			lua.Globals["Random"] = new LuaRandom();
			lua.Globals["Timer"] = new LuaTimer(this);
			lua.Globals["File"] = UserData.CreateStatic<LuaFile>();
			lua.Globals["Networking"] = new LuaNetworking(this);
			lua.Globals["WayPoint"] = UserData.CreateStatic<WayPoint>();
			lua.Globals["SpawnType"] = UserData.CreateStatic<SpawnType>();
			lua.Globals["ChatMessageType"] = UserData.CreateStatic<ChatMessageType>();
			lua.Globals["ServerLog_MessageType"] = UserData.CreateStatic<ServerLog.MessageType>();
			lua.Globals["Submarine"] = UserData.CreateStatic<Submarine>();
			lua.Globals["Client"] = UserData.CreateStatic<Client>();
			lua.Globals["Character"] = UserData.CreateStatic<Character>();
			lua.Globals["Item"] = UserData.CreateStatic<Item>();
			lua.Globals["Level"] = UserData.CreateStatic<Level>();
			lua.Globals["PositionType"] = UserData.CreateStatic<Level.PositionType>();
			lua.Globals["JobPrefab"] = UserData.CreateStatic<JobPrefab>();
			lua.Globals["TraitorMessageType"] = UserData.CreateStatic<TraitorMessageType>();
			lua.Globals["CauseOfDeathType"] = UserData.CreateStatic<CauseOfDeathType>();
			lua.Globals["AfflictionPrefab"] = UserData.CreateStatic<AfflictionPrefab>();
			lua.Globals["CharacterTeamType"] = UserData.CreateStatic<CharacterTeamType>();		
			lua.Globals["Vector2"] = UserData.CreateStatic<Vector2>();
			lua.Globals["Vector3"] = UserData.CreateStatic<Vector3>();
			lua.Globals["Vector4"] = UserData.CreateStatic<Vector3>();
			lua.Globals["CreateVector2"] = (Func<float, float, Vector2>)CreateVector2;
			lua.Globals["CreateVector3"] = (Func<float, float, float, Vector3>)CreateVector3;
			lua.Globals["CreateVector4"] = (Func<float, float, float, float, Vector4>)CreateVector4;
			lua.Globals["ChatMessage"] = UserData.CreateStatic<ChatMessage>();
			lua.Globals["Hull"] = UserData.CreateStatic<Hull>();
			lua.Globals["InvSlotType"] = UserData.CreateStatic<InvSlotType>();
			lua.Globals["Gap"] = UserData.CreateStatic<Gap>();
			lua.Globals["ContentPackage"] = UserData.CreateStatic<ContentPackage>();
			lua.Globals["ClientPermissions"] = UserData.CreateStatic<ClientPermissions>();

			List<string> modulePaths = new List<string>();
			foreach (ContentPackage d in GameMain.Config.AllEnabledPackages.ToArray())
			{
				string goodpath = d.Path.Remove(d.Path.IndexOf("/filelist.xml"),13);
				modulePaths.Add(goodpath + "/Lua/?");

				if (Directory.Exists(goodpath + "/Lua/Autorun"))
				{
					luaScriptLoader.RunFolder(goodpath + "/Lua/Autorun");
				}
			}

			luaScriptLoader.ModulePaths = modulePaths.ToArray();

		}

	}



}

