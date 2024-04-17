using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine;
using BepInEx.Logging;
using BepInEx;


namespace ChatTerminal
{
    [BepInPlugin(modGUID, modName, modVersion)]
    public class ChatTerminalPlugin : BaseUnityPlugin
    {
        // Mod Details
        private const string modGUID = "bmatusiask.ChatTerminal";
        private const string modName = "ChatTerminal";
        private const string modVersion = "1.0.0.0";

        private readonly Harmony harmony = new Harmony(modGUID);
        public static BaseUnityPlugin Instance { get; private set; }
        private ManualLogSource Log { get; set; }

        void Awake()
        {
            if (Instance == null)
                Instance = this;

            Log = BepInEx.Logging.Logger.CreateLogSource(modName);
            Log.LogInfo("Patching");

            harmony.PatchAll(typeof(PluginPatch));
        }

    }

    internal class PluginPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(HUDManager), "Update")]
        private static void Update_Post(ref TMP_InputField ___chatTextField, ref HUDElement ___Chat)
        {
            if (___chatTextField.isActiveAndEnabled)
            {
                if (___Chat.targetAlpha == 1.0f)
                {
                    ___chatTextField.placeholder.color = Color.white;
                }
                else
                {
                    ___chatTextField.placeholder.color = Color.blue;
                }
                ___chatTextField.textComponent.color = Color.white;
                if (!usedTerminalThisSession)
                {
                    OnSubmit("");
                }
            }
        }

        [HarmonyPatch(typeof(HUDManager), "SubmitChat_performed")]
        [HarmonyPrefix]
        private static void SubmitChat_performed_Pre()
        {
            if (!HUDManager.Instance.localPlayer.isTypingChat) return;
            string cmd_text = HUDManager.Instance.chatTextField.text;

            if (cmd_text.StartsWith("/"))
            {
                HUDManager.Instance.chatTextField.text = cmd_text.Substring(1);
                return;
            }
            else
            {
                HUDManager.Instance.localPlayer.isTypingChat = false;
                HUDManager.Instance.chatTextField.text = "";
                EventSystem.current.SetSelectedGameObject(null);
                HUDManager.Instance.typingIndicator.enabled = false;
            }

            OnSubmit(cmd_text);
        }
        public static bool DetectBang(Terminal terminal)
        {
            if (terminal.textAdded == 0)
            {
                return false;
            }
            string text = terminal.screenText.text;
            int textAdded = terminal.textAdded;
            int length = text.Length;
            int num = length - textAdded;
            string text2 = text.Substring(num, length - num);
            return text2[text2.Length - 1] == '!';
        }

        private static void OnSubmit(string cmd_text = null)
        {


            string output = "";
            bool bangDectected = false;
            bool reRunAgain = false;
            string lastText;
            string[] array = new string[] { };
            if (cmd_text != null) array = cmd_text.Split(new char[1] { ' ' });

            FieldInfo usedTerminalThisSessionField = terminal.GetType().GetField("usedTerminalThisSession", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            bool usedTerminalThisSession = (bool)usedTerminalThisSessionField.GetValue(terminal);
            if (!usedTerminalThisSession)
            {
                LoadNewNode(terminal.terminalNodes.specialNodes[1]);
                output = ExtractTerminalOutput("", terminal.screenText.text);
                AddMessage(output.Trim());
            }
            if (!usedTerminalThisSession)
            {
                usedTerminalThisSessionField.SetValue(terminal, true);
                FieldInfo syncedTerminalValuesField = terminal.GetType().GetField("usedTerminalThisSession", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                bool syncedTerminalValues = (bool)syncedTerminalValuesField.GetValue(terminal);
                if (!syncedTerminalValues)
                {
                    terminal.SyncTerminalValuesServerRpc();
                }
                LoadNewNode(terminal.terminalNodes.specialNodes[13]);
                output = ExtractTerminalOutput("", terminal.screenText.text);
                if (cmd_text != null && cmd_text.Length > 0)
                    reRunAgain = true;
            }
            else if (cmd_text != null)
            {
                if (array[0] == "clear")
                {
                    HUDManager.Instance.ChatMessageHistory.Clear();
                    HUDManager.Instance.chatText.text = "";
                    HUDManager.Instance.lastChatMessage = "";
                    return;
                }
                else if (array[0] == "?")
                {
                    lastText = terminal.screenText.text;
                    LoadNewNode(terminal.terminalNodes.specialNodes[13]);
                    output = ExtractTerminalOutput(lastText, terminal.screenText.text);
                }
                else
                {
                    terminal.screenText.text += cmd_text;
                    bangDectected = DetectBang(terminal);
                    if (terminal.currentNode != null && terminal.currentNode.acceptAnything)
                    {
                        LoadNewNode(terminal.currentNode.terminalOptions[0].result);
                    }
                    else
                    {
                        TerminalNode terminalNode = ParsePlayerSentence();
                        if (terminalNode != null)
                        {
                            lastText = terminal.screenText.text;
                            if (terminalNode.buyRerouteToMoon == -2)
                            {
                                totalCostOfItems = terminalNode.itemCost;
                            }
                            else if (terminalNode.itemCost != 0)
                            {
                                totalCostOfItems = terminalNode.itemCost * terminal.playerDefinedAmount;
                            }
                            if (terminalNode.buyItemIndex != -1 || (terminalNode.buyRerouteToMoon != -1 && terminalNode.buyRerouteToMoon != -2) || terminalNode.shipUnlockableID != -1)
                            {
                                LoadNewNodeIfAffordable(terminalNode);
                            }
                            else if (terminalNode.creatureFileID != -1)
                            {
                                AttemptLoadCreatureFileNode(terminalNode);
                            }
                            else if (terminalNode.storyLogFileID != -1)
                            {
                                AttemptLoadStoryLogFileNode(terminalNode);
                            }
                            else
                            {
                                LoadNewNode(terminalNode, cmd_text);
                            }
                            output = ExtractTerminalOutput(lastText, terminal.screenText.text);
                        }
                        else
                        {
                            terminal.screenText.text = terminal.screenText.text.Substring(0, terminal.screenText.text.Length - terminal.textAdded);
                            terminal.currentText = terminal.screenText.text;
                            terminal.textAdded = 0;
                        }

                    }

                }
            }
            if (output != "")
            {
                AddMessage(output.Trim());
                if (bangDectected)
                {
                    OnSubmit("CONFIRM");
                }
            }
            if (reRunAgain)
                OnSubmit(cmd_text);
        }


        private static string ExtractTerminalOutput(string previous, string output)
        {
            if (output.StartsWith(previous))
            {
                output = output.Substring(previous.Length);
            }
            return output;
        }


        private static void LoadNewNode(TerminalNode terminalNode, string name = "PROGRAMED")
        {

            terminal.LoadNewNode(terminalNode);
        }
        private static Terminal terminal
        {
            get
            {
                return (Terminal)UnityEngine.Object.FindObjectOfType(typeof(Terminal));
            }
            set { }
        }
        private static int totalCostOfItems
        {
            get
            {
                FieldInfo totalCostOfItems_FieldInfo = terminal.GetType().GetField("totalCostOfItems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return (int)totalCostOfItems_FieldInfo.GetValue(terminal);
            }
            set
            {
                FieldInfo totalCostOfItems_FieldInfo = terminal.GetType().GetField("totalCostOfItems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                totalCostOfItems_FieldInfo.SetValue(terminal, value);
            }
        }

        private static bool usedTerminalThisSession
        {
            get
            {
                FieldInfo theFieldInfo = terminal.GetType().GetField("usedTerminalThisSession", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return (bool)theFieldInfo.GetValue(terminal);
            }
            set
            {
                FieldInfo theFieldInfo = terminal.GetType().GetField("usedTerminalThisSession", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                theFieldInfo.SetValue(terminal, value);
            }
        }

        private static TerminalNode ParsePlayerSentence()
        {
            MethodInfo ParsePlayerSentence = terminal.GetType().GetMethod("ParsePlayerSentence", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (TerminalNode)ParsePlayerSentence.Invoke(terminal, null);
        }
        private static void LoadNewNodeIfAffordable(TerminalNode terminalNode)
        {
            MethodInfo LoadNewNodeIfAffordable = terminal.GetType().GetMethod("LoadNewNodeIfAffordable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            LoadNewNodeIfAffordable.Invoke(terminal, new object[] { terminalNode });
        }
        private static void AttemptLoadCreatureFileNode(TerminalNode terminalNode)
        {
            MethodInfo AttemptLoadCreatureFileNode = terminal.GetType().GetMethod("AttemptLoadCreatureFileNode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            AttemptLoadCreatureFileNode.Invoke(terminal, new object[] { terminalNode });
        }
        private static void AttemptLoadStoryLogFileNode(TerminalNode terminalNode)
        {
            MethodInfo AttemptLoadStoryLogFileNode = terminal.GetType().GetMethod("AttemptLoadStoryLogFileNode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            AttemptLoadStoryLogFileNode.Invoke(terminal, new object[] { terminalNode });
        }

        private static void AddMessage(string chatMessage)
        {
            if (!(HUDManager.Instance.lastChatMessage == chatMessage))
            {
                HUDManager.Instance.lastChatMessage = chatMessage;
                HUDManager.Instance.PingHUDElement(HUDManager.Instance.Chat, 10f);

                string item = "<color=#FFFF00>" + chatMessage + "</color>";
                HUDManager.Instance.ChatMessageHistory.Add(item);
                HUDManager.Instance.chatText.text = "";
                for (int i = 0; i < HUDManager.Instance.ChatMessageHistory.Count; i++)
                {
                    TextMeshProUGUI textMeshProUGUI = HUDManager.Instance.chatText;
                    textMeshProUGUI.text = textMeshProUGUI.text + "\n" + HUDManager.Instance.ChatMessageHistory[i];
                }
            }
        }
    }

}
