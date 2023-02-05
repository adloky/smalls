#include <FastLED.h>
#include <ESP8266WiFi.h>
#include <ESP8266WebServer.h>

#define NUM_LEDS 100
#define NUM_MAX 11
CRGB leds[NUM_LEDS];
CRGB colors[8] = { CRGB::Black, CRGB::Blue, CRGB::Green, CRGB::Cyan,
    CRGB::Red, CRGB::Magenta, CRGB::Yellow, CRGB::White };
int nums[NUM_MAX+1];

const char* ssid = "Anton2";  // SSID
const char* password = "02111958"; // пароль
ESP8266WebServer server(80);

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
    FastLED.show();
    delay(10);
    FastLED.show();
}

void apply(char* s) {
    for (byte i = 0; i < NUM_LEDS; i++) {
        leds[i] = CRGB::Black;
    }

    split(s);

    if (nums[0] == -1 || nums[1] == -1) {
        show();
        return;
    }

    CRGB color = colors[nums[0]];
    for (byte i = 1; nums[i] > -1; i++) {
        leds[nums[i]] = color;
    }

    show();
}

void handleRequest() {
    String s = server.arg("q");
    char* ca = "                                                  ";
    s.toCharArray(ca, 50);
    apply(ca);
    server.send(200, "text/plain", ca);
}

void setup() {
    FastLED.addLeds<NEOPIXEL, D5>(leds, NUM_LEDS);
    Serial.begin(9600);
    delay(1000);

    apply("");

    WiFi.begin(ssid, password);
    while (WiFi.status() != WL_CONNECTED) {
        delay(1000);
    }
    server.on("/", handleRequest);
    server.begin();
    Serial.println("Server listening");
}

void loop() {
    server.handleClient();
}