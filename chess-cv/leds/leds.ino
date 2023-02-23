#include <FastLED.h>
#include <ESP8266WiFi.h>
#include <ESP8266WebServer.h>

#define LED_MAX 100
#define NUM_MAX 11
CRGB leds[LED_MAX];
CRGB colors[8] = { 0x000000, 0x0000FF, 0xFF0000, 0xFF00FF,
    0x00FF00, 0x00FFFF, 0xFFFF00, 0xFFFFFF };
int nums[NUM_MAX + 1];

const char* ssid = "Anton2";  // SSID
const char* password = "02111958"; // пароль
ESP8266WebServer server(80);

void test_leds() {
    apply("7-1-2-3-4-5-6-7-8-9-10-11"); delay(1000);
    apply("7-12-13-14-15-16-17-18-19-20-21-22"); delay(1000);
    apply("7-23-24-25-26-27-28-29-30-31-32-33"); delay(1000);
    apply("7-34-35-36-37-38-39-40-41-42-43-44"); delay(1000);
    apply("7-45-46-47-48-49-50-51-52-53-54-55"); delay(1000);
    apply("7-56-57-58-59-60-61-62-63-64-65-66"); delay(1000);
    apply("7-67-68-69-70-71-72-73-74-75-76-77"); delay(1000);
    apply("7-78-79-80-81-82-83-84-85-86-87-88"); delay(1000);
    apply("7-89-90-91-92-93-94-95-96-97-98-99"); delay(1000);
    apply("");
}

void split(char* s) {
    if (s[0] == 0) {
        nums[0] = -1;
        return;        
    }

    int i,j;
    char* ss = &s[0];

    for (i = 0, j = 0; s[i] != 0 && j < NUM_MAX-1; i++) {
        if (s[i] != '-') continue;

        s[i] = 0;
        nums[j] = atoi(ss);
        s[i] = '-';
        ss = &s[i+1];
        j++;
    }
    nums[j] = atoi(ss);
    nums[j+1] = -1;
}

void show() {
    noInterrupts();
    FastLED.show();
    interrupts();
}

void apply(char* s) {
    split(s);

    bool on_leds[LED_MAX];
    for (byte i = 0; i < LED_MAX; i++) {
        on_leds[i] = false;
    }

    for (byte i = 0; i < NUM_MAX && nums[i] > -1; i++) {
        if (i == 0) continue;
        on_leds[nums[i]] = true;
    }

    for (byte i = 0; i < LED_MAX; i++) {
        if (on_leds[i]) continue;
        // bool is_changed = leds[i] != colors[0];
        leds[i] = colors[0];
        // if (is_changed) show();
    }
    show();

    if (nums[0] < 0 || nums[1] < 0) return;

    CRGB color = colors[nums[0]];
    for (byte i = 1; nums[i] > -1; i++) {
        // bool is_changed = leds[nums[i]] != color;
        leds[nums[i]] = color;
        // if (is_changed) show();
    }
    show();
}

void handleRequest() {
    String s = server.arg("q");
    char ca[(NUM_MAX + 1) * 3];
    s.toCharArray(ca, sizeof(ca));
    server.send(200, "text/plain", ca);
    apply(ca);
}

void clear() {
    FastLED.clear(true);
    show();
}

void setup() {
    FastLED.addLeds<NEOPIXEL, D8>(leds, LED_MAX);
    Serial.begin(9600);
    delay(1000);

    clear();
    
    WiFi.begin(ssid, password);
    while (WiFi.status() != WL_CONNECTED) {
        delay(1000);
    }
    server.on("/", handleRequest);
    server.begin();

    test_leds();
}

void loop() {
    server.handleClient();
}