/*
The MIT License (MIT)

Copyright (c) 2018 Giovanni Paolo Vigano'

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;
using System.Security.Cryptography.X509Certificates;
using System.IO;

/// <summary>
/// Adaptation for Unity of the M2MQTT library (https://github.com/eclipse/paho.mqtt.m2mqtt),
/// modified to run on UWP (also tested on Microsoft HoloLens).
/// </summary>
namespace M2MqttUnity
{
    /// <summary>
    /// Generic MonoBehavior wrapping a MQTT client, using a double buffer to postpone message processing in the main thread. 
    /// </summary>
    public class M2MqttUnityClient : MonoBehaviour
    {
        [Header("MQTT broker configuration")]
        [Tooltip("IP address or URL of the host running the broker")]
        public string brokerAddress = "172.20.10.2";
        [Tooltip("Port where the broker accepts connections")]
        public int brokerPort = 8883;
        [Tooltip("Use encrypted connection")]
        public bool isEncrypted = true;
        [Header("Connection parameters")]
        [Tooltip("Connection to the broker is delayed by the the given milliseconds")]
        public int connectionDelay = 500;
        [Tooltip("Connection timeout in milliseconds")]
        public int timeoutOnConnection = MqttSettings.MQTT_CONNECT_TIMEOUT;
        [Tooltip("Connect on startup")]
        public bool autoConnect = false;
        [Tooltip("UserName for the MQTT broker. Keep blank if no user name is required.")]
        public string mqttUserName = null;
        [Tooltip("Password for the MQTT broker. Keep blank if no password is required.")]
        public string mqttPassword = null;
        [Tooltip("Client identifier sent to the broker. Leave blank to use a random GUID.")]
        public string mqttClientId = "";

        [Header("mTLS Configuration")]
        [Tooltip("Drag your ca.bytes file here (rename ca.crt → ca.bytes)")]
        public TextAsset caCertAsset;
        [Tooltip("Drag your visualizer.bytes file here (rename visualizer.pfx → visualizer.bytes)")]
        public TextAsset clientPfxAsset;
        public string pfxPassword = "cegb18";
        
        /// <summary>
        /// Wrapped MQTT client
        /// </summary>
        protected MqttClient client;

        private List<MqttMsgPublishEventArgs> messageQueue1 = new List<MqttMsgPublishEventArgs>();
        private List<MqttMsgPublishEventArgs> messageQueue2 = new List<MqttMsgPublishEventArgs>();
        private List<MqttMsgPublishEventArgs> frontMessageQueue = null;
        private List<MqttMsgPublishEventArgs> backMessageQueue = null;
        private bool mqttClientConnectionClosed = false;
        private bool mqttClientConnected = false;

        /// <summary>
        /// Event fired when a connection is successfully established
        /// </summary>
        public event Action ConnectionSucceeded;
        /// <summary>
        /// Event fired when failing to connect
        /// </summary>
        public event Action ConnectionFailed;

        /// <summary>
        /// Connect to the broker using current settings.
        /// </summary>
        public virtual void Connect()
        {
            if (client == null || !client.IsConnected)
            {
                StartCoroutine(DoConnect());
            }
        }

        /// <summary>
        /// Disconnect from the broker, if connected.
        /// </summary>
        public virtual void Disconnect()
        {
            if (client != null)
            {
                StartCoroutine(DoDisconnect());
            }
        }

        /// <summary>
        /// Override this method to take some actions before connection (e.g. display a message)
        /// </summary>
        protected virtual void OnConnecting()
        {
            Debug.LogFormat("Connecting to broker on {0}:{1}...\n", brokerAddress, brokerPort.ToString());
        }

        /// <summary>
        /// Override this method to take some actions if the connection succeeded.
        /// </summary>
        protected virtual void OnConnected()
        {
            Debug.LogFormat("Connected to {0}:{1}...\n", brokerAddress, brokerPort.ToString());

            SubscribeTopics();

            if (ConnectionSucceeded != null)
            {
                ConnectionSucceeded();
            }
        }

        /// <summary>
        /// Override this method to take some actions if the connection failed.
        /// </summary>
        protected virtual void OnConnectionFailed(string errorMessage)
        {
            Debug.LogWarning("Connection failed.");
            if (ConnectionFailed != null)
            {
                ConnectionFailed();
            }
        }

        /// <summary>
        /// Override this method to subscribe to MQTT topics.
        /// </summary>
        protected virtual void SubscribeTopics()
        {
        }

        /// <summary>
        /// Override this method to unsubscribe to MQTT topics (they should be the same you subscribed to with SubscribeTopics() ).
        /// </summary>
        protected virtual void UnsubscribeTopics()
        {
        }

        /// <summary>
        /// Disconnect before the application quits.
        /// </summary>
        protected virtual void OnApplicationQuit()
        {
            CloseConnection();
        }

        /// <summary>
        /// Initialize MQTT message queue
        /// Remember to call base.Awake() if you override this method.
        /// </summary>
        protected virtual void Awake()
        {
            frontMessageQueue = messageQueue1;
            backMessageQueue = messageQueue2;
        }

        /// <summary>
        /// Connect on startup if autoConnect is set to true.
        /// </summary>
        protected virtual void Start()
        {
            if (autoConnect)
            {
                Connect();
            }
        }

        /// <summary>
        /// Override this method for each received message you need to process.
        /// </summary>
        protected virtual void DecodeMessage(string topic, byte[] message)
        {
            Debug.LogFormat("Message received on topic: {0}", topic);
        }

        /// <summary>
        /// Override this method to take some actions when disconnected.
        /// </summary>
        protected virtual void OnDisconnected()
        {
            Debug.Log("Disconnected.");
        }

        /// <summary>
        /// Override this method to take some actions when the connection is closed.
        /// </summary>
        protected virtual void OnConnectionLost()
        {
            Debug.LogWarning("CONNECTION LOST!");
        }

        /// <summary>
        /// Processing of income messages and events is postponed here in the main thread.
        /// Remember to call ProcessMqttEvents() in Update() method if you override it.
        /// </summary>
        protected virtual void Update()
        {
            ProcessMqttEvents();
        }

        protected virtual void ProcessMqttEvents()
        {
            // process messages in the main queue
            SwapMqttMessageQueues();
            ProcessMqttMessageBackgroundQueue();
            // process messages income in the meanwhile
            SwapMqttMessageQueues();
            ProcessMqttMessageBackgroundQueue();

            if (mqttClientConnectionClosed)
            {
                mqttClientConnectionClosed = false;
                OnConnectionLost();
            }
        }

        private void ProcessMqttMessageBackgroundQueue()
        {
            foreach (MqttMsgPublishEventArgs msg in backMessageQueue)
            {
                DecodeMessage(msg.Topic, msg.Message);
            }
            backMessageQueue.Clear();
        }

        /// <summary>
        /// Swap the message queues to continue receiving message when processing a queue.
        /// </summary>
        private void SwapMqttMessageQueues()
        {
            frontMessageQueue = frontMessageQueue == messageQueue1 ? messageQueue2 : messageQueue1;
            backMessageQueue = backMessageQueue == messageQueue1 ? messageQueue2 : messageQueue1;
        }

        private void OnMqttMessageReceived(object sender, MqttMsgPublishEventArgs msg)
        {
            frontMessageQueue.Add(msg);
        }

        private void OnMqttConnectionClosed(object sender, EventArgs e)
        {
            // Set unexpected connection closed only if connected (avoid event handling in case of controlled disconnection)
            mqttClientConnectionClosed = mqttClientConnected;
            mqttClientConnected = false;
        }

        /// <summary>
        /// Connects to the broker using the current settings.
        /// </summary>
        /// <returns>The execution is done in a coroutine.</returns>
        private IEnumerator DoConnect()
        {
            // wait for the given delay
            yield return new WaitForSecondsRealtime(connectionDelay / 1000f);
            // leave some time to Unity to refresh the UI
            yield return new WaitForEndOfFrame();

            // create client instance 
            if (client == null)
            {
                try
                {
#if (!UNITY_EDITOR && UNITY_WSA_10_0 && !ENABLE_IL2CPP)
                    client = new MqttClient(brokerAddress,brokerPort,isEncrypted, isEncrypted ? MqttSslProtocols.SSLv3 : MqttSslProtocols.None);
#else
                    X509Certificate caCert = null;
                    X509Certificate clientCert = null;

                    if (isEncrypted)
                    {
                        if (caCertAsset != null)
                        {
                            caCert = new X509Certificate2(caCertAsset.bytes);
                        }
                        else
                        {
                            Debug.LogWarning("[M2Mqtt] Ca Cert Asset is not assigned in the Inspector.");
                        }

                        if (clientPfxAsset != null)
                        {
                            clientCert = new X509Certificate2(clientPfxAsset.bytes, pfxPassword,
                                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
                        }
                        else
                        {
                            Debug.LogWarning("[M2Mqtt] Client Pfx Asset is not assigned in the Inspector.");
                        }
                    }

                    client = new MqttClient(brokerAddress, brokerPort, isEncrypted, caCert, clientCert, isEncrypted ? MqttSslProtocols.TLSv1_2 : MqttSslProtocols.None);
#endif
                }
                catch (Exception e)
                {
                    client = null;
                    Debug.LogError("[M2Mqtt] MqttClient init FAILED");
                    LogFullException(e);
                    OnConnectionFailed(e.Message);
                    yield break;
                }
            }
            else if (client.IsConnected)
            {
                yield break;
            }
            OnConnecting();

            // leave some time to Unity to refresh the UI
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            client.Settings.TimeoutOnConnection = timeoutOnConnection;
            string clientId = string.IsNullOrEmpty(mqttClientId) ? Guid.NewGuid().ToString() : mqttClientId;
            try
            {
                client.Connect(clientId, mqttUserName, mqttPassword);
            }
            catch (Exception e)
            {
                client = null;
                Debug.LogError("[M2Mqtt] broker.Connect() FAILED");
                LogFullException(e);
                OnConnectionFailed(e.Message);
                yield break;
            }
            if (client.IsConnected)
            {
                client.ConnectionClosed += OnMqttConnectionClosed;
                // register to message received 
                client.MqttMsgPublishReceived += OnMqttMessageReceived;
                mqttClientConnected = true;
                OnConnected();
            }
            else
            {
                OnConnectionFailed("CONNECTION FAILED!");
            }
        }

        private IEnumerator DoDisconnect()
        {
            yield return new WaitForEndOfFrame();
            CloseConnection();
            OnDisconnected();
        }
void LogFullException(Exception e)
        {
            if (e == null) 
            {
                Debug.LogError("Exception is null");
                return;
            }
            
            Debug.LogError("Exception Type: " + e.GetType().FullName);
            Debug.LogError("Exception Message: " + (e.Message ?? "no message"));
            
            if (!string.IsNullOrEmpty(e.StackTrace))
            {
                string[] stackLines = e.StackTrace.Split('\n');
                Debug.LogError("Stack Trace (" + stackLines.Length + " lines):");
                foreach (string line in stackLines)
                {
                    if (!string.IsNullOrEmpty(line.Trim()))
                        Debug.LogError("  " + line.Trim());
                }
            }
            
            Exception inner = e.InnerException;
            int depth = 1;
            while (inner != null && depth < 10)
            {
                Debug.LogError("--- INNER EXCEPTION #" + depth + " ---");
                Debug.LogError("Type: " + inner.GetType().FullName);
                Debug.LogError("Message: " + (inner.Message ?? "no message"));
                
                if (!string.IsNullOrEmpty(inner.StackTrace))
                {
                    string[] innerStack = inner.StackTrace.Split('\n');
                    Debug.LogError("Inner Stack (" + innerStack.Length + " lines):");
                    for (int i = 0; i < System.Math.Min(innerStack.Length, 5); i++)
                    {
                        if (!string.IsNullOrEmpty(innerStack[i].Trim()))
                            Debug.LogError("  " + innerStack[i].Trim());
                    }
                }
                
                inner = inner.InnerException;
                depth++;
            }
            
            Debug.LogError("=== END EXCEPTION DETAILS ===");
        }

        private static string GetFullException(Exception e)
        {
            if (e == null) return "null exception";
            
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            Exception current = e;
            int depth = 0;
            
            while (current != null && depth < 10)
            {
                if (depth > 0) sb.AppendLine().Append("  INNER EXCEPTION: ");
                
                sb.Append(current.GetType().FullName);
                sb.Append(": ");
                sb.Append(current.Message ?? "no message");
                
                if (!string.IsNullOrEmpty(current.StackTrace))
                {
                    sb.AppendLine().Append("  Stack: ");
                    string[] stackLines = current.StackTrace.Split('\n');
                    for (int i = 0; i < System.Math.Min(stackLines.Length, 3); i++)
                    {
                        sb.AppendLine().Append("    ").Append(stackLines[i].Trim());
                    }
                }
                
                current = current.InnerException;
                depth++;
            }
            
            return sb.ToString();
        }

        private void CloseConnection()
        {
            mqttClientConnected = false;
            if (client != null)
            {
                if (client.IsConnected)
                {
                    UnsubscribeTopics();
                    client.Disconnect();
                }
                client.MqttMsgPublishReceived -= OnMqttMessageReceived;
                client.ConnectionClosed -= OnMqttConnectionClosed;
                client = null;
            }
        }

#if ((!UNITY_EDITOR && UNITY_WSA_10_0))
        private void OnApplicationFocus(bool focus)
        {
            // On UWP 10 (HoloLens) we cannot tell whether the application actually got closed or just minimized.
            // (https://forum.unity.com/threads/onapplicationquit-and-ondestroy-are-not-called-on-uwp-10.462597/)
            if (focus)
            {
                Connect();
            }
            else
            {
                CloseConnection();
            }
        }
#endif
    }
}
