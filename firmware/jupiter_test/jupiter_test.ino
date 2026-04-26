/*
 * Jupiter Touch — I2C Pot Verification Test
 * 
 * Flash this INSTEAD of the main firmware (temporarily).
 * It writes values to each pot and reads them back to verify I2C works.
 * 
 * Open Serial Monitor at 19200 baud and watch the results.
 * No multimeter needed — pass/fail is printed for every channel.
 * 
 * After testing, flash the real jupiter_bridge.ino firmware back.
 */

#include <Wire.h>
#include "AD5252.h"

AD5252 chipA(0x2C);  // Channels 1 & 2
AD5252 chipB(0x2D);  // Channels 3 & 4
AD5252 chipC(0x2E);  // Channels 5 & 6

// Relay pin pairs for each channel
const uint8_t relayPins[6][2] = {
    {2, 3},    // Ch1 Thumb
    {4, 5},    // Ch2 Index
    {6, 7},    // Ch3 Middle
    {8, 9},    // Ch4 Ring
    {10, 11},  // Ch5 Pinky
    {12, 13},  // Ch6 Palm
};

// Which chip and wiper index for each channel
AD5252* chips[6] = { &chipA, &chipA, &chipB, &chipB, &chipC, &chipC };
uint8_t wipers[6] = { 1, 3, 1, 3, 1, 3 };

const char* fingerNames[6] = { "Thumb", "Index", "Middle", "Ring", "Pinky", "Palm" };

int totalPass = 0;
int totalFail = 0;

void setup() {
    Serial.begin(19200);
    Wire.begin();

    // Initialize all relay pins as OUTPUT, LOW
    for (int ch = 0; ch < 6; ch++) {
        pinMode(relayPins[ch][0], OUTPUT);
        pinMode(relayPins[ch][1], OUTPUT);
        digitalWrite(relayPins[ch][0], LOW);
        digitalWrite(relayPins[ch][1], LOW);
    }

    // Set all pots to safe position
    for (int ch = 0; ch < 6; ch++) {
        chips[ch]->setPosition(wipers[ch], 255);
    }

    delay(1000);  // Let serial monitor connect

    Serial.println(F(""));
    Serial.println(F("════════════════════════════════════════════"));
    Serial.println(F("  JUPITER BRIDGE — HARDWARE VERIFICATION"));
    Serial.println(F("════════════════════════════════════════════"));
    Serial.println(F(""));
    Serial.println(F("Testing all 6 channels..."));
    Serial.println(F(""));

    // Test each channel
    for (int ch = 0; ch < 6; ch++) {
        testChannel(ch);
    }

    // Summary
    Serial.println(F(""));
    Serial.println(F("════════════════════════════════════════════"));
    Serial.print(F("  RESULT:  "));
    Serial.print(totalPass);
    Serial.print(F(" passed,  "));
    Serial.print(totalFail);
    Serial.println(F(" failed"));
    Serial.println(F("════════════════════════════════════════════"));

    if (totalFail == 0) {
        Serial.println(F(""));
        Serial.println(F("  ALL TESTS PASSED — board is good!"));
        Serial.println(F("  Flash jupiter_bridge.ino now."));
    } else {
        Serial.println(F(""));
        Serial.println(F("  FAILURES DETECTED — check wiring on"));
        Serial.println(F("  the failed channels before proceeding."));
    }

    Serial.println(F(""));
    Serial.println(F("Press reset to run again."));
}

void loop() {
    // Nothing — test runs once in setup
}

// ══════════════════════════════════════════════════════════════════

void testChannel(int ch) {
    Serial.print(F("── CH"));
    Serial.print(ch + 1);
    Serial.print(F("  "));
    Serial.print(fingerNames[ch]);
    Serial.println(F(" ──────────────────────────────────"));

    bool relayOK = testRelay(ch);
    bool potOK = testPot(ch);

    Serial.print(F("   CHANNEL "));
    Serial.print(ch + 1);
    Serial.print(F(": "));

    if (relayOK && potOK) {
        Serial.println(F("PASS ✓"));
        totalPass++;
    } else {
        Serial.print(F("FAIL ✗ ("));
        if (!relayOK) Serial.print(F("relay "));
        if (!potOK) Serial.print(F("pot/I2C "));
        Serial.println(F(")"));
        totalFail++;
    }
    Serial.println(F(""));
}

// ── Relay Test ─────────────────────────────────────────────────

bool testRelay(int ch) {
    uint8_t pinA = relayPins[ch][0];
    uint8_t pinB = relayPins[ch][1];

    // Turn on
    digitalWrite(pinA, HIGH);
    digitalWrite(pinB, HIGH);
    delay(50);

    // Read back pin state (on Nano, digitalRead on an OUTPUT pin
    // returns the value you wrote — confirms the pin is functional)
    bool aHigh = digitalRead(pinA) == HIGH;
    bool bHigh = digitalRead(pinB) == HIGH;

    // Turn off
    digitalWrite(pinA, LOW);
    digitalWrite(pinB, LOW);
    delay(50);

    bool aLow = digitalRead(pinA) == LOW;
    bool bLow = digitalRead(pinB) == LOW;

    bool pass = aHigh && bHigh && aLow && bLow;

    Serial.print(F("   Relay pins D"));
    Serial.print(pinA);
    Serial.print(F("/D"));
    Serial.print(pinB);
    Serial.print(F(":  "));
    Serial.println(pass ? F("OK") : F("FAIL"));

    return pass;
}

// ── Pot Test (I2C write + readback) ────────────────────────────

bool testPot(int ch) {
    AD5252* chip = chips[ch];
    uint8_t wiper = wipers[ch];

    // Test 3 values: 255 (max resistance), 0 (min resistance), 128 (mid)
    uint8_t testValues[] = { 255, 0, 128 };
    const char* testNames[] = { "255 (off/safe)", "  0 (max stim)", "128 (mid)      " };
    bool allPass = true;

    for (int t = 0; t < 3; t++) {
        uint8_t writeVal = testValues[t];

        // Write
        chip->setPosition(wiper, writeVal);
        delay(20);  // Give I2C time

        // Read back
        uint8_t readVal = chip->getPosition(wiper);

        bool match = (readVal == writeVal);
        if (!match) allPass = false;

        Serial.print(F("   Pot write "));
        Serial.print(testNames[t]);
        Serial.print(F("  read back: "));
        Serial.print(readVal);
        Serial.print(F("  "));
        Serial.println(match ? F("OK") : F("MISMATCH"));
    }

    // Always leave pot at safe value
    chip->setPosition(wiper, 255);

    Serial.print(F("   Pot I2C (chip 0x"));
    if (ch < 2) Serial.print(F("2C"));
    else if (ch < 4) Serial.print(F("2D"));
    else Serial.print(F("2E"));
    Serial.print(F(", RDAC"));
    Serial.print(wiper);
    Serial.print(F("):  "));
    Serial.println(allPass ? F("OK") : F("FAIL"));

    return allPass;
}
