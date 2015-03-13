﻿
using System;
using System.Collections.Generic;
using System.Collections;
using System.Text;
using System.Threading; //SLEEP
using System.IO;


namespace IW4MAdmin
{
    class Server
    {
        const int FLOOD_TIMEOUT = 300;

        public Server(string address, int port, string password)
        {
            IP = address;
            Port = port;
            rcon_pass = password;
            clientnum = 0;
            RCON = new RCON(this);
            logFile = new file("admin_" + port + ".log", true);
#if DEBUG
            Log = new Log(logFile, Log.Level.Debug);
#else
            Log = new Log(logFile, Log.Level.Production);
#endif
            players = new List<Player>(new Player[18]);
            clientDB = new ClientsDB("clients.rm");
            statDB = new StatsDB("stats_" + port + ".rm");
            aliasDB = new AliasesDB("aliases.rm");
            Bans = clientDB.getBans();
            owner = clientDB.getOwner();
            events = new Queue<Event>();
            HB = new Heartbeat(this);
            Macros = new Dictionary<String, Object>();
            nextMessage = 0;
            initCommands();
            initMacros();
            initMessages();
            initMaps();
            initRules();
        }

        //Returns the current server name -- *STRING*
        public String getName()
        {
            return hostname;
        }

        public String getMap()
        {
            return mapname;
        }

        //Returns current server IP set by `net_ip` -- *STRING*
        public String getIP()
        {
            return IP;
        }

        //Returns current server port set by `net_port` -- *INT*
        public int getPort()
        {
            return Port;
        }

        //Returns number of active clients on server -- *INT*
        public int getNumPlayers()
        {
            return clientnum;
        }

        //Returns the list of commands
        public List<Command> getCommands()
        {
            return commands;
        }

        //Returns list of all current players
        public List<Player> getPlayers()
        {
            return players;
        }

        public int getClientNum()
        {
            return clientnum;
        }

        public int getMaxClients()
        {
            return maxClients;
        }

        //Returns list of all active bans (loaded at runtime)
        public List<Ban> getBans()
        {
            return Bans;
        }
            
        //Add player object p to `players` list
        public bool addPlayer(Player P)
        {
            try
            {
                if (clientDB.getPlayer(P.getID(), P.getClientNum()) == null)
                {
                    clientDB.addPlayer(P);
                    Player New = clientDB.getPlayer(P.getID(), P.getClientNum());
                    statDB.addPlayer(New);
                    aliasDB.addPlayer(new Aliases(New.getDBID(), New.getName(), New.getIP()));
                }


                //messy way to prevent loss of last event
                Player NewPlayer = clientDB.getPlayer(P.getID(), P.getClientNum());
                NewPlayer.stats = statDB.getStats(NewPlayer.getDBID());
                if (NewPlayer.stats == null)
                {
                    statDB.addPlayer(NewPlayer);
                    NewPlayer.stats = statDB.getStats(NewPlayer.getDBID());
                }
                NewPlayer.Alias = aliasDB.getPlayer(NewPlayer.getDBID());
                NewPlayer.lastEvent = P.lastEvent;
                
                if (players[NewPlayer.getClientNum()] == null)
                {
                    try
                    {
                        P.updateIP(IPS[P.getID()]);
                    }

                    catch
                    {
                        Log.Write("Looks like the connecting player doesn't have an IP location assigned yet.", Log.Level.Debug);
                        P.updateIP(getPlayerIP(P.getID()));
                    }

                    if (P.getName() != NewPlayer.getName())
                    {
                        NewPlayer.updateName(P.getName());
                        NewPlayer.Alias.addName(P.getName());
                    }

                    if (P.getIP() != NewPlayer.getIP())
                    {
                        NewPlayer.updateIP(P.getIP());
                        NewPlayer.Alias.addIP(P.getIP());
                    }

                    clientnum++;
                }

                NewPlayer.lastEvent = P.lastEvent;

                players[NewPlayer.getClientNum()] = null;
                players[NewPlayer.getClientNum()] = NewPlayer;

                Ban B = isBanned(NewPlayer);
                if (NewPlayer.getLevel() == Player.Permission.Banned || B != null)
                {
                    Log.Write("Banned client " + P.getName() + " trying to connect...", Log.Level.Debug);
                    String Message = "^1Player Kicked: ^7Previously Banned for ^5" + B.getReason();
                    P.Kick(Message);
                }

                else
                    Log.Write("Client " + NewPlayer.getName() + " connecting...", Log.Level.Debug);

                clientDB.updatePlayer(NewPlayer);
                aliasDB.updatePlayer(NewPlayer.Alias);

                return true;
            }
            catch (Exception E)
            {
                Log.Write("Unable to add player " + P.getName() + " - " + E.Message, Log.Level.Debug);
                return false;
            }
        }

        //Remove player by CLIENT NUMBER
        public bool removePlayer(int cNum)
        {
            Log.Write("Updating stats for " + players[cNum].getName(), Log.Level.Debug);
            statDB.updatePlayer(players[cNum]);
            Log.Write("Client at " + cNum + " disconnecting...", Log.Level.Debug);
            players[cNum] = null;
            clientnum--;
            return true;
        }

        //Get a client from players list by by log line. If create = true, it will return  a new player object
         public Player clientFromLine(String[] line, int name_pos, bool create)
         {
            string Name = line[name_pos].ToString().Trim();
            if (create)
            {
                Player C = new Player(Name, line[1].ToString(), Convert.ToInt16(line[2]), 0);
                return C;
            }

            else
            {
                foreach (Player P in players)
                {
                    if (P == null)
                        continue; 

                    if (line[1].Trim() == P.getID())
                        return P;
                }

                Log.Write("Could not find player but player is in server. Lets try to manually add (looks like you didn't start me on an empty server)", Log.Level.All);
                addPlayer(new Player(Name, line[1].ToString(), Convert.ToInt16(line[2]), 0));
                return players[Convert.ToInt16(line[2])];
            }
        }

        //Should be client from Name ( returns client in players list by name )
         public Player clientFromLine(String Name)
         {
             foreach (Player P in players)
             {
                 if (P == null)
                     continue;
                 if (P.getName().ToLower().Contains(Name.ToLower()))
                     return P;
             }

             return null;
         }

         public Ban isBanned(Player C)
         {
                 foreach (Ban B in Bans)
                 {
                 
                     if (B.getID() == C.getID())
                         return B;

                     if (B.getIP() == null)
                         continue;

                     if (C.Alias.getIPS().Find(f => f.Contains(B.getIP())) != null)
                         return B;

                     if (C.getIP() == B.getIP())
                         return B;
                 }

            return null;
         }

        public String getPlayerIP(String GUID)
        {
            Dictionary<string, string> dict;
            int count = 0;
            do
            {
               //because rcon can be weird
               dict = Utilities.IPFromStatus(RCON.addRCON("status"));
               count++;
            } while(dict.Count < clientnum || count < 5);
            return dict[GUID];
        }

        public Command processCommand(Event E, Command C)
        {
            E.Data = Utilities.removeWords(E.Data, 1);
            String[] Args = E.Data.Trim().Split(' ');
   
            if (Args.Length < (C.getNumArgs()))
            {
                E.Origin.Tell("Not enough arguments supplied!");
                return null;
            }

            if(E.Origin.getLevel() < C.getNeededPerm())
            {
                E.Origin.Tell("You do not have access to that command!");
                return null;
            }

            if (C.needsTarget())
            {
                int cNum = -1;
                int.TryParse(Args[0], out cNum);

                if (Args[0] == String.Empty)
                    return C;

                if (Args[0][0] == '@')
                {
                    int dbID = -1;
                    int.TryParse(Args[0].Substring(1, Args[0].Length-1), out dbID);
                    Player found = E.Owner.clientDB.getPlayer(dbID);
                    if (found != null)
                        E.Target = found;
                }

                else if(Args[0].Length < 3 && cNum > -1 && cNum < 18)
                {
                    if (players[cNum] != null)
                        E.Target = players[cNum];
                }

                else
                    E.Target = clientFromLine(Args[0]);

                if (E.Target == null)
                {
                    E.Origin.Tell("Unable to find specified player.");
                    return null;
                }
            }
            return C;
        }

        private void addEvent(Event E)
        {
            events.Enqueue(E);
        }

        private void manageEventQueue()
        {
            while (true)
            {
                if (events.Count > 0)
                {
                    processEvent(events.Peek());
                    events.Dequeue();
                }
                Utilities.Wait(0.1);
            }
        }

        //Starts the monitoring process
        public void Monitor()
        {

            //Handles new rcon requests in a fashionable manner
            Thread RCONQueue = new Thread(new ThreadStart(RCON.ManageRCONQueue));
            RCONQueue.Start();

            if (!intializeBasics())
            {
                Log.Write("Stopping " + Port + " due to uncorrectable errors (check log)" + logPath, Log.Level.Production);
                Utilities.Wait(10);
                return;
            }

            //Handles new events in a fashionable manner
            Thread eventQueue = new Thread(new ThreadStart(manageEventQueue));
            eventQueue.Start();

            int timesFailed = 0;
            long l_size = -1;
            String[] lines = new String[8];
            String[] oldLines = new String[8];
            DateTime start = DateTime.Now;

            Utilities.Wait(1);
            Broadcast("IW4M Admin is now ^2ONLINE");

            while (errors <=5)
            {
                try
                {
                    lastMessage = DateTime.Now - start;
                    if(lastMessage.TotalSeconds > messageTime && messages.Count > 0)
                    {

                        if  (RCON.responseSendRCON("sv_online") == null)
                        {
                            timesFailed++;
                            Log.Write("Server appears to be offline - " + timesFailed, Log.Level.Debug);
                        }
                        else
                            timesFailed = 0;
                        Thread.Sleep(300);
                        initMacros();
                        Broadcast(Utilities.processMacro(Macros, messages[nextMessage]));
                        if (nextMessage == (messages.Count - 1))
                            nextMessage = 0;
                        else
                            nextMessage++;
                        start = DateTime.Now;
                        if (timesFailed <= 3)
                            HB.Send();
                    }

                    if (l_size != logFile.getSize())
                    {
                        lines = logFile.Tail(8);
                        if (lines != oldLines)
                        {
                            l_size = logFile.getSize();
                            int end;
                            if (lines.Length == oldLines.Length)
                                end = lines.Length - 1;
                            else
                                end = Math.Abs((lines.Length - oldLines.Length)) - 1;

                            for (int count = 0; count < lines.Length; count++)
                            {
                                if (lines.Length < 1 && oldLines.Length < 1)
                                    continue;

                                if (lines[count] == oldLines[oldLines.Length - 1])
                                    continue;

                                if (lines[count].Length < 10) //Not a needed line 
                                    continue;

                                else
                                {
                                    string[] game_event = lines[count].Split(';');
                                    Event event_ = Event.requestEvent(game_event, this);
                                    if (event_ != null)
                                    {
                                        if (event_.Origin == null)
                                            event_.Origin = new Player("WORLD", "-1", -1, 0);

                                        event_.Origin.lastEvent = event_;
                                        event_.Origin.lastEvent.Owner = this;

                                        addEvent(event_);
                                    }
                                }

                            }
                        }
                    }
                    oldLines = lines;
                    l_size = logFile.getSize();
                    Thread.Sleep(1);
                }
                catch (Exception E)
                {
                    Log.Write("Something unexpected occured. Hopefully we can ignore it - " + E.Message + " @"  + Utilities.GetLineNumber(E), Log.Level.All);
                    errors++;
                    continue;
                }

            }

            RCONQueue.Abort();
            eventQueue.Abort();

        }

        //Vital RCON commands to establish log file and server name. May need to cleanup in the future
        private bool intializeBasics()
        {
           try
            {
                String[] infoResponse = RCON.addRCON("getstatus");
    
                if (infoResponse == null || infoResponse.Length < 2)
                {
                    Log.Write("Could not get server status!", Log.Level.All);
                    return false;
                }

                infoResponse = infoResponse[1].Split('\\');
                Dictionary<String, String> infoResponseDict = new Dictionary<string, string>();

                for (int i = 0; i < infoResponse.Length; i++)
                {
                    if (i%2 == 0 || infoResponse[i] == String.Empty)
                        continue;
                    infoResponseDict.Add(infoResponse[i], infoResponse[i+1]);
                }

                mapname = infoResponseDict["mapname"];
                try
                {
                    mapname = maps.Find(m => m.Name.Equals(mapname)).Alias;
                }

                catch(Exception)
                {
                    Log.Write(mapname + " doesn't appear to be in the maps.cfg", Log.Level.Debug);
                }

                hostname = Utilities.stripColors(infoResponseDict["sv_hostname"]);
                IW_Ver = infoResponseDict["shortversion"];
                maxClients = Convert.ToInt32(infoResponseDict["sv_maxclients"]);
                Gametype = infoResponseDict["g_gametype"];
                try
                {
                    Website = infoResponseDict["_Website"];
                }
                catch (Exception E)
                {
                    Log.Write("Seems not to have website specified", Log.Level.Debug);
                }

                String[] p =RCON.addRCON("fs_basepath");

                if (p == null)
                {
                    Log.Write("Could not obtain basepath!", Log.Level.All);
                    return false;
                }

                p = p[1].Split('"');
                Basepath = p[3].Substring(0, p[3].Length - 2).Trim();
                p = null;
                //END

                //get fs_game
                p =RCON.addRCON("fs_game");

                if (p == null)
                {
                    Log.Write("Could not obtain mod path!", Log.Level.All);
                    return false;
                }

                p = p[1].Split('"');
                Mod = p[3].Substring(0, p[3].Length - 2).Trim().Replace('/', '\\');
                p = null;

                //END

                //get g_log
                p = RCON.addRCON("g_log");

                if (p == null)
                {
                    Log.Write("Could not obtain log path!", Log.Level.All);
                    return false;
                }

                if (p.Length < 4)
                {
                    Thread.Sleep(FLOOD_TIMEOUT);
                    Log.Write("Server does not appear to have map loaded. Please map_rotate", Log.Level.All);
                    return false;
                }

                p = p[1].Split('"');
                string log = p[3].Substring(0, p[3].Length - 2).Trim();
                p = null;

                //END

                //get g_logsync
                p =RCON.addRCON("g_logsync");

                if (p == null)
                {
                    Log.Write("Could not obtain log sync status!", Log.Level.All);
                    return false;
                }


                p = p[1].Split('"');
                int logsync = Convert.ToInt32(p[3].Substring(0, p[3].Length - 2).Trim());
                p = null;

                if (logsync != 1)
                    RCON.addRCON("g_logsync 1");
                //END

                //get iw4m_onelog
                p =RCON.addRCON("iw4m_onelog");

                if (p[0] == String.Empty || p[1].Length < 15)
                {
                    Log.Write("Could not obtain iw4m_onelog value!", Log.Level.All);
                    return false;
                }

                p = p[1].Split('"');
                string onelog = p[3].Substring(0, p[3].Length - 2).Trim();
                p = null;
                //END

                if (Mod == String.Empty || onelog == "1")
                    logPath = Basepath + '\\' + "m2demo" + '\\' + log;
                else
                    logPath = Basepath + '\\' + Mod + '\\' + log;

                if (!File.Exists(logPath))
                {
                    Log.Write("Gamelog does not exist!", Log.Level.All);
                    return false;
                }

                logFile = new file(logPath);
                Log.Write("Log file is " + logPath, Log.Level.Debug);

               //get players ip's 
                p =RCON.addRCON("status");
                if (p == null)
                {
                   Log.Write("Unable to get initial player list!", Log.Level.Debug);
                   return false;
                }
               
                IPS = Utilities.IPFromStatus(p);

#if DEBUG
               /* System.Net.FtpWebRequest tmp = (System.Net.FtpWebRequest)System.Net.FtpWebRequest.Create("ftp://raidmax.org/logs/games_old.log");
                tmp.Credentials = new System.Net.NetworkCredential("*", "*");
                System.IO.Stream ftpStream = tmp.GetResponse().GetResponseStream();
                String ftpLog = new StreamReader(ftpStream).ReadToEnd();*/
                logPath = "games_old.log"; 
#endif

                return true;
            }
            catch (Exception E)
            {
                Log.Write("Error during initialization - " + E.Message, Log.Level.All);
                return false;
            }
        }

        //Process any server event
        public bool processEvent(Event E)
        {
            if (E.Type == Event.GType.Connect)
            {
                addPlayer(E.Origin);
                return true;
            }

            if (E.Type == Event.GType.Disconnect && E.Origin.getClientNum() > 0)
            {
                if (getNumPlayers() > 0 && E.Origin != null && players[E.Origin.getClientNum()] != null)
                {
                    clientDB.updatePlayer(E.Origin);
                    removePlayer(E.Origin.getClientNum());
                }
                return true;
            }

            if (E.Type == Event.GType.Kill)
            {
                if (E.Origin != null && E.Target != null && E.Origin.stats != null)
                {
                    E.Origin.stats.Kills++;
                    E.Origin.stats.updateKDR();
                    E.Origin.stats.updateSkill(E.Target.stats.Skill);

                    E.Target.stats.Deaths++;
                    E.Target.stats.updateKDR();
                    //E.Target.stats.updateSkill(E.Origin.stats.Skill);
                }
            }

            if (E.Type == Event.GType.Say && E.Origin != null)
            {
                if (E.Data.Length < 2)
                    return false;

                Log.Write("Message from " + E.Origin.getName() + ": " + E.Data, Log.Level.Debug);

                if (E.Data.Substring(0, 1) != "!")
                    return true;

                Command C = E.isValidCMD(commands);
                if (C != null)
                {
                    C = processCommand(E, C);
                    if (C != null)
                    {
                        C.Execute(E);
                        return true;
                    }
                    else
                    {
                        Log.Write("Error processing command by " + E.Origin.getName(), Log.Level.Debug);
                        return true;
                    }
                }

                else
                    E.Origin.Tell("You entered an invalid command!");

                return true;
            }

            if (E.Type == Event.GType.MapChange)
            {
                Log.Write("New map loaded", Log.Level.Debug);
                String[] statusResponse = E.Data.Split('\\');
                if (statusResponse.Length >= 15 && statusResponse[13] == "mapname")
                    mapname = maps.Find(m => m.Name.Equals(statusResponse[14])).Alias;    //update map for heartbeat                    
            }

            if (E.Type == Event.GType.MapEnd)
            {
                Log.Write("Game ending...", Log.Level.Debug);
                foreach (Player P in players)
                {
                    if (P == null || P.stats == null)
                        continue;
                    statDB.updatePlayer(P);
                    Log.Write("Updated stats for client " + P.getDBID(), Log.Level.Debug);
                }
                return true;
            }

            return false;
        }

        public bool Reload()
        {
            try
            {
                messages = null;
                maps = null;
                rules = null;
                initMaps();
                initMessages();
                initRules();
                return true;
            }
            catch (Exception E)
            {
                Log.Write("Unable to reload configs! - " + E.Message, Log.Level.Debug);
                messages = new List<String>();
                maps = new List<Map>();
                rules = new List<String>();
                return false;
            }
        }

        //THESE MAY NEED TO BE MOVED
        public void Broadcast(String Message)
        {
            RCON.addRCON("sayraw " + Message);
        }

        public void Tell(String Message, Player Target)
        {
            RCON.addRCON("tell " + Target.getClientNum() + " " + Message + "^7");
        }

        public void Kick(String Message, Player Target)
        {
            RCON.addRCON("clientkick " + Target.getClientNum() + " \"" + Message + "^7\"");
        }

        public void Ban(String Message, Player Target, Player Origin)
        {
            RCON.addRCON("tempbanclient " + Target.getClientNum() + " \"" + Message + "^7\"");
            if (Origin != null)
            {
                Target.setLevel(Player.Permission.Banned);
                Ban newBan = new Ban(Target.getLastO(), Target.getID(), Origin.getID(), DateTime.Now, Target.getIP());
                Bans.Add(newBan);
                clientDB.addBan(newBan);
                clientDB.updatePlayer(Target);
            }
        }

        public bool Unban(String GUID, Player Target)
        {
            foreach (Ban B in Bans)
            {
                if (B.getID() == Target.getID())
                {
                    clientDB.removeBan(GUID);
                    Bans.Remove(B);
                    Player P = clientDB.getPlayer(Target.getID(), 0);
                    P.setLevel(Player.Permission.User);
                    clientDB.updatePlayer(P);
                    return true;
                }
            }
            return false;
        }


        public void fastRestart(int delay)
        {
            Utilities.Wait(delay);
            RCON.addRCON("fast_restart");
        }

        public void mapRotate(int delay)
        {
            Utilities.Wait(delay);
            RCON.addRCON("map_rotate");
        }

        public void tempBan(String Message, Player Target)
        {
            RCON.addRCON("tempbanclient " + Target.getClientNum() + " \"" + Message + "\"");
        }
        
        public void mapRotate()
        {
            RCON.addRCON("map_rotate");
        }

        public void Map(String map)
        {
            RCON.addRCON("map " + map);
        }

        public String Wisdom()
        {
            String Quote = new Connection("http://www.iheartquotes.com/api/v1/random?max_lines=1&max_characters=200").Read();
            return Utilities.removeNastyChars(Quote);
        }

        //END

        //THIS IS BAD BECAUSE WE DON"T WANT EVERYONE TO HAVE ACCESS :/
        public String getPassword()
        {
            return rcon_pass;
        }

        private void initMacros()
        {
            Macros = new Dictionary<String, Object>();
            Macros.Add("WISDOM", Wisdom());
            Macros.Add("TOTALPLAYERS", clientDB.totalPlayers());
        }

        private void initMaps()
        {
            maps = new List<Map>();

            file mapfile = new file("config\\maps.cfg");
            String[] _maps = mapfile.readAll();
            mapfile.Close();
            if (_maps.Length > 2) // readAll returns minimum one empty string
            {
                foreach (String m in _maps)
                {
                    String[] m2 = m.Split(':');
                    if (m2.Length > 1)
                    {
                        Map map = new Map(m2[0].Trim(), m2[1].Trim());
                        maps.Add(map);
                    }
                }
            }     
            else
                Log.Write("Maps configuration appears to be empty - skipping...", Log.Level.All);
        }

        private void initMessages()
        {
            messages = new List<String>();

            file messageCFG = new file("config\\messages.cfg");
            String[] lines = messageCFG.readAll();
            messageCFG.Close();

            if (lines.Length < 2) //readAll returns minimum one empty string
            {
                Log.Write("Messages configuration appears empty - skipping...", Log.Level.All);
                return;
            }

            int mTime = -1;
            int.TryParse(lines[0], out mTime);

            if (messageTime == -1)
                messageTime = 60;
            else
                messageTime = mTime;
            
            foreach (String l in lines)
            {
                if (lines[0] != l && l.Length > 1)
                    messages.Add(l);
            }
            if (Program.Version != Program.latestVersion && Program.latestVersion != 0)
               messages.Add("^5IW4M Admin ^7is outdated. Please ^5update ^7to version " + Program.latestVersion);
        }

        private void initRules()
        {
            rules = new List<String>();

            file ruleFile = new file("config\\rules.cfg");
            String[] _rules = ruleFile.readAll();
            ruleFile.Close();
            if (_rules.Length > 2) // readAll returns minimum one empty string
            {
                foreach (String r in _rules)
                {
                    if (r.Length > 1)
                        rules.Add(r);
                }
            }
            else
                Log.Write("Rules configuration appears empty - skipping...", Log.Level.All);
        }

        private void initCommands()
        {
            // Something like *COMMAND* | NAME | HELP MSG | ALIAS | NEEDED PERMISSION | # OF REQUIRED ARGS | HAS TARGET |

            commands = new List<Command>();

            if(owner == null)
                commands.Add(new Owner("owner", "claim ownership of the server", "owner", Player.Permission.User, 0, false));

            commands.Add(new Kick("kick", "kick a player by name. syntax: !kick <player> <reason>.", "k", Player.Permission.Moderator, 2, true));
            commands.Add(new Say("say", "broadcast message to all players. syntax: !say <message>.", "s", Player.Permission.Moderator, 1, false));
            commands.Add(new TempBan("tempban", "temporarily ban a player for 1 hour. syntax: !tempban <player> <reason>.", "tb", Player.Permission.Moderator, 2, true));
            commands.Add(new SBan("ban", "permanently ban a player from the server. syntax: !ban <player> <reason>", "b", Player.Permission.SeniorAdmin, 2, true));
            commands.Add(new WhoAmI("whoami", "give information about yourself. syntax: !whoami.", "who", Player.Permission.User, 0, false));
            commands.Add(new List("list", "list active clients syntax: !list.", "l", Player.Permission.Moderator, 0, false));
            commands.Add(new Help("help", "list all available commands. syntax: !help.", "h", Player.Permission.User, 0, false));
            commands.Add(new FastRestart("fastrestart", "fast restart current map. syntax: !fastrestart.", "fr", Player.Permission.Moderator, 0, false));
            commands.Add(new MapRotate("maprotate", "cycle to the next map in rotation. syntax: !maprotate.", "mr", Player.Permission.Administrator, 0, false));
            commands.Add(new SetLevel("setlevel", "set player to specified administration level. syntax: !setlevel <player> <level>.", "sl", Player.Permission.Owner, 2, true));
            commands.Add(new Usage("usage", "get current application memory usage. syntax: !usage.", "us", Player.Permission.Moderator, 0, false));
            commands.Add(new Uptime("uptime", "get current application running time. syntax: !uptime.", "up", Player.Permission.Moderator, 0, false));
            commands.Add(new Warn("warn", "warn player for infringing rules syntax: !warn <player> <reason>.", "w", Player.Permission.Moderator, 2, true));
            commands.Add(new WarnClear("warnclear", "remove all warning for a player syntax: !warnclear <player>.", "wc", Player.Permission.Administrator, 1, true));
            commands.Add(new Unban("unban", "unban player by guid. syntax: !unban <guid>.", "ub", Player.Permission.SeniorAdmin, 1, true));
            commands.Add(new Admins("admins", "list currently connected admins. syntax: !admins.", "a", Player.Permission.User, 0, false));
            commands.Add(new Wisdom("wisdom", "get a random wisdom quote. syntax: !wisdom", "w", Player.Permission.Administrator, 0, false));
            commands.Add(new MapCMD("map", "change to specified map. syntax: !map", "m", Player.Permission.Administrator, 1, false));
            commands.Add(new Find("find", "find player in database. syntax: !find <player>", "f", Player.Permission.SeniorAdmin, 1, false));
            commands.Add(new Rules("rules", "list server rules. syntax: !rules", "r", Player.Permission.User, 0, false));
            commands.Add(new PrivateMessage("privatemessage", "send message to other player. syntax: !pm <player> <message>", "pm", Player.Permission.User, 2, true));
            commands.Add(new _Stats("stats", "view your stats or another player's. syntax: !stats", "xlrstats", Player.Permission.User, 0, true));
            commands.Add(new TopStats("topstats", "view the top 4 players on this server. syntax: !topstats", "xlrtopstats", Player.Permission.User, 0, false));
            commands.Add(new Reload("reload", "reload configurations. syntax: !reload", "reload", Player.Permission.Owner, 0, false));
            /*
            commands.Add(new commands { command = "stats", desc = "view your server stats.", requiredPer = 0 });
            commands.Add(new commands { command = "speed", desc = "change player speed. syntax: !speed <number>", requiredPer = 3 });
            commands.Add(new commands { command = "gravity", desc = "change game gravity. syntax: !gravity <number>", requiredPer = 3 });

            commands.Add(new commands { command = "version", desc = "view current app version.", requiredPer = 0 });*/
        }

        //Objects
        public Log Log;
        public RCON RCON;
        public ClientsDB clientDB;
        public AliasesDB aliasDB;
        public StatsDB statDB;
        public List<Ban> Bans;
        public Player owner;
        public List<Map> maps;
        public List<String> rules;
        public Queue<Event> events;
        public Heartbeat HB;
        public String Website;
        public String Gametype;

        //Info
        private String IP;
        private int Port;
        private String hostname;
        private String mapname;
        private int clientnum;
        private string rcon_pass;
        private List<Player> players;
        private List<Command> commands;
        private List<String> messages;
        private int messageTime;
        private TimeSpan lastMessage;
        private int nextMessage;
        private int errors = 0;
        private String IW_Ver;
        private int maxClients;
        private Dictionary<String, Object> Macros;


        //Will probably move this later
        private Dictionary<String, String> IPS;

     
        //Log stuff
        private String Basepath;
        private String Mod;
        private String logPath;
        private file logFile;
    }
}