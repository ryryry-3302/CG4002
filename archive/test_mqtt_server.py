import asyncio
import logging
import socket
import json
import time
import threading
import os
from amqtt.broker import Broker
import paho.mqtt.client as mqtt

# Logging setup
logging.basicConfig(level=logging.WARNING, format='%(name)s - %(levelname)s - %(message)s')
logger = logging.getLogger("TestDashboard")
logger.setLevel(logging.INFO)

# Suppress AMQTT verbose logging
logging.getLogger("amqtt").setLevel(logging.WARNING)
logging.getLogger("transitions").setLevel(logging.WARNING)

# Topics
SUBSCRIBE_TOPICS = [
    "/control/visualizer",
    "orchestra/app_state",
    "ar/right/cmd",
    "orchestra/test",
    "orchestra/left_gesture_event",
    "orchestra/stick_stroke"
]

PUBLISH_LEFT_GESTURE = "orchestra/left_gesture_event"
PUBLISH_RIGHT_STICK = "orchestra/stick_stroke"
PUBLISH_STATUS = "orchestra/system_status"

def get_local_ip():
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        # doesn't even have to be reachable
        s.connect(('10.255.255.255', 1))
        IP = s.getsockname()[0]
    except Exception:
        IP = '127.0.0.1'
    finally:
        s.close()
    return IP

# --- AMQTT Broker Configuration ---
config = {
    'listeners': {
        'default': {
            'type': 'tcp',
            'bind': '0.0.0.0:1883'
        }
    },
    'sys_interval': 10,
    'auth': {
        'allow-anonymous': True,
        'plugins': ['auth_anonymous']
    },
    'topic-check': {
        'enabled': True,
        'plugins': ['topic_acl'],
        'acl': {
            'anonymous': ['#']
        }
    }
}

# --- Paho MQTT Client ---
def on_connect(client, userdata, flags, rc):
    logger.info(f"Test Dashboard connected to local broker with result code {rc}")
    for topic in SUBSCRIBE_TOPICS:
        client.subscribe(topic)
        logger.info(f"Subscribed to {topic}")

def on_message(client, userdata, msg):
    try:
        payload = msg.payload.decode('utf-8')
        print(f"\n[INBOUND MQTT] {msg.topic}: {payload}")
    except Exception as e:
        print(f"\n[INBOUND MQTT] {msg.topic}: <Binary Data>")

def paho_thread_func(local_ip):
    client = mqtt.Client("TestDashboard")
    client.on_connect = on_connect
    client.on_message = on_message
    
    # Wait for broker to start
    time.sleep(2)
    try:
        client.connect("127.0.0.1", 1883, 60)
        client.loop_start()
        
        # Interactive CLI
        print("\n\n" + "="*60)
        print("MQTT TEST DASHBOARD READY")
        print("="*60)
        print("Available Commands:")
        print(" 1 - Send LEFT Gesture (UP)")
        print(" 2 - Send LEFT Gesture (DOWN)")
        print(" 3 - Send LEFT Gesture (LEFT)")
        print(" 4 - Send LEFT Gesture (RIGHT)")
        print(" w - Send RIGHT DRUMSTICK (UPSTROKE)")
        print(" s - Send RIGHT DRUMSTICK (DOWNSTROKE)")
        print(" q - Quit")
        print("="*60 + "\n")

        while True:
            try:
                cmd = input().strip().lower()
            except EOFError:
                break
                
            if cmd == 'q':
                break
            elif cmd == '1':
                payload = {"gestureId": "UP", "isClenched": True, "confidence": 0.99, "timestamp": int(time.time()*1000)}
                client.publish(PUBLISH_LEFT_GESTURE, json.dumps(payload))
                print(f"-> Sent UP gesture.")
            elif cmd == '2':
                payload = {"gestureId": "DOWN", "isClenched": True, "confidence": 0.99, "timestamp": int(time.time()*1000)}
                client.publish(PUBLISH_LEFT_GESTURE, json.dumps(payload))
                print(f"-> Sent DOWN gesture.")
            elif cmd == '3':
                payload = {"gestureId": "LEFT", "isClenched": True, "confidence": 0.99, "timestamp": int(time.time()*1000)}
                client.publish(PUBLISH_LEFT_GESTURE, json.dumps(payload))
                print(f"-> Sent LEFT gesture.")
            elif cmd == '4':
                payload = {"gestureId": "RIGHT", "isClenched": True, "confidence": 0.99, "timestamp": int(time.time()*1000)}
                client.publish(PUBLISH_LEFT_GESTURE, json.dumps(payload))
                print(f"-> Sent RIGHT gesture.")
            elif cmd == 'w':
                payload = {"type": "UPSTROKE", "timestamp": int(time.time()*1000)}
                client.publish(PUBLISH_RIGHT_STICK, json.dumps(payload))
                print(f"-> Sent UPSTROKE on {PUBLISH_RIGHT_STICK}")
            elif cmd == 's':
                payload = {"type": "DOWNSTROKE", "timestamp": int(time.time()*1000)}
                client.publish(PUBLISH_RIGHT_STICK, json.dumps(payload))
                print(f"-> Sent DOWNSTROKE on {PUBLISH_RIGHT_STICK}")
            elif cmd != '':
                print("Unknown command. 1/2/3/4/w/s/q supported.")

    except Exception as e:
        logger.error(f"Dashboard client error: {e}")
    finally:
        client.loop_stop()
        os._exit(0) # Force exit if paho loop exits

async def start_broker():
    broker = Broker(config)
    await broker.start()
    return broker

def main():
    local_ip = get_local_ip()
    print("\n" + "*"*60)
    print(f"Starting local MQTT Broker on {local_ip}:1883...")
    print(f"--> UNITY SETUP: Enter {local_ip} in the MQTTManager Inspector!")
    print("*"*60 + "\n")
    
    # Start paho client in background thread
    threading.Thread(target=paho_thread_func, args=(local_ip,), daemon=True).start()
    
    # Start broker loop
    loop = asyncio.get_event_loop()
    try:
        broker = loop.run_until_complete(start_broker())
        loop.run_forever()
    except KeyboardInterrupt:
        print("\nShutting down...")
    finally:
        # Loop will exit here, process will eventually stop
        pass

if __name__ == '__main__':
    main()
