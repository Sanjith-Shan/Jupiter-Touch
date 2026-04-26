/*
 * Jupiter Touch — Arduino Nano Firmware v2.0
 * 6-channel EMS haptic device controller
 *
 * Hardware:
 *   3x AD5252 digital pots on I2C: 0x2C, 0x2D, 0x2E (each has RDAC1 + RDAC3)
 *   6x photo-relay pairs on digital pins D2–D13
 *
 * Channel map:
 *   Ch1 Thumb  — D2/D3,   AD5252@0x2C RDAC1
 *   Ch2 Index  — D4/D5,   AD5252@0x2C RDAC3
 *   Ch3 Middle — D6/D7,   AD5252@0x2D RDAC1
 *   Ch4 Ring   — D8/D9,   AD5252@0x2D RDAC3
 *   Ch5 Pinky  — D10/D11, AD5252@0x2E RDAC1
 *   Ch6 Palm   — D12/D13, AD5252@0x2E RDAC3
 *
 * Serial protocol (19200 baud, newline-terminated):
 *
 *   CxIy    — Set channel x (1–6) to pot value y (0–255) and activate relay.
 *             0 = maximum stimulation, 255 = minimum.
 *             Example: "C1I128\n" sets thumb to mid intensity and activates.
 *
 *   CxON    — Activate channel x relay without changing intensity.
 *             Example: "C1ON\n" activates thumb relay at current pot value.
 *
 *   CxOFF   — Deactivate channel x: ramp pot to 255 then open relay.
 *             Example: "C2OFF\n" deactivates index finger.
 *
 *   ALLOFF  — Emergency stop: deactivate all 6 channels immediately.
 *
 *   STATUS  — Query all 6 channels. Also resets the safety timeout.
 *             Response: CH1:ON:128 CH2:OFF:255 CH3:ON:0 ...
 *
 * Safety:
 *   - All relays OFF and pots at 255 on power-up.
 *   - Deactivation always resets pot to 255 before opening relay.
 *   - 15-second silence timeout: if no serial command is received for 15 seconds
 *     while any channel is active, all channels are automatically killed.
 *     Send any command (including STATUS) to reset the timer.
 *
 * Legacy single-char commands (backward compat with openEMSstim):
 *   '1' toggle Ch1 relay, '2' toggle Ch2 relay
 *   'q' Ch1 intensity++, 'a' Ch1 intensity--
 *   'w' Ch2 intensity++, 's' Ch2 intensity--
 */

#include <Wire.h>
#include "AD5252.h"
#include "EMSChannel.h"

// ── Potentiometer chips ────────────────────────────────────────────────────
AD5252 potiA(0x2C);  // channels 1 & 2
AD5252 potiB(0x2D);  // channels 3 & 4
AD5252 potiC(0x2E);  // channels 5 & 6

// ── EMS channels (0-indexed internally, 1-indexed in commands) ─────────────
//                    pin1  pin2  chip    wiper
EMSChannel ch[6] = {
    EMSChannel(2,  3,  &potiA, 1),   // Ch1 Thumb
    EMSChannel(4,  5,  &potiA, 3),   // Ch2 Index
    EMSChannel(6,  7,  &potiB, 1),   // Ch3 Middle
    EMSChannel(8,  9,  &potiB, 3),   // Ch4 Ring
    EMSChannel(10, 11, &potiC, 1),   // Ch5 Pinky
    EMSChannel(12, 13, &potiC, 3),   // Ch6 Palm
};

// ── Safety timeout ─────────────────────────────────────────────────────────
const unsigned long TIMEOUT_MS = 15000;  // 15 seconds
unsigned long lastCommandTime = 0;
bool timeoutTriggered = false;

// Legacy intensity tracking for single-char commands
uint8_t legacyIntensity[2] = {200, 200};

// ── Helpers ────────────────────────────────────────────────────────────────

void allOff() {
    for (int i = 0; i < 6; i++) ch[i].deactivate();
}

bool anyChannelActive() {
    for (int i = 0; i < 6; i++) {
        if (ch[i].isActive()) return true;
    }
    return false;
}

void printStatus() {
    for (int i = 0; i < 6; i++) {
        if (i > 0) Serial.print(' ');
        Serial.print(F("CH"));
        Serial.print(i + 1);
        Serial.print(':');
        Serial.print(ch[i].isActive() ? F("ON") : F("OFF"));
        Serial.print(':');
        Serial.print(ch[i].getIntensity());
    }
    Serial.println();
}

// ── Setup ──────────────────────────────────────────────────────────────────
void setup() {
    Wire.begin();

    // Ensure all pots are at safe (off) position before enabling relays
    potiA.setPosition(1, POT_OFF);
    potiA.setPosition(3, POT_OFF);
    potiB.setPosition(1, POT_OFF);
    potiB.setPosition(3, POT_OFF);
    potiC.setPosition(1, POT_OFF);
    potiC.setPosition(3, POT_OFF);

    for (int i = 0; i < 6; i++) ch[i].begin();

    Serial.begin(19200);
    Serial.setTimeout(50);

    lastCommandTime = millis();

    Serial.println(F("JupiterTouch v2.0 — 6ch ready"));
    Serial.println(F("Commands: CxIy CxON CxOFF ALLOFF STATUS"));
    Serial.println(F("Safety: 15s timeout active"));
}

// ── Command parser ─────────────────────────────────────────────────────────
void processCommand(String& cmd) {
    cmd.trim();
    if (cmd.length() == 0) return;

    // Any valid command resets the safety timeout
    lastCommandTime = millis();
    timeoutTriggered = false;

    // ── ALLOFF ─────────────────────────────────────────────────────────────
    if (cmd.equalsIgnoreCase("ALLOFF")) {
        allOff();
        Serial.println(F("ALL OFF"));
        return;
    }

    // ── STATUS ─────────────────────────────────────────────────────────────
    if (cmd.equalsIgnoreCase("STATUS")) {
        printStatus();
        return;
    }

    // ── CxIy / CxON / CxOFF ───────────────────────────────────────────────
    if (cmd.charAt(0) == 'C' || cmd.charAt(0) == 'c') {
        int offIdx = cmd.indexOf("OFF");
        int onIdx  = cmd.indexOf("ON");
        int iIdx   = cmd.indexOf('I');

        // CxOFF — deactivate channel
        if (offIdx > 0) {
            int chNum = cmd.substring(1, offIdx).toInt();
            if (chNum >= 1 && chNum <= 6) {
                ch[chNum - 1].deactivate();
                Serial.print(F("CH"));
                Serial.print(chNum);
                Serial.println(F(" OFF"));
            }
            return;
        }

        // CxON — activate relay without changing intensity
        if (onIdx > 0 && iIdx < 0) {
            int chNum = cmd.substring(1, onIdx).toInt();
            if (chNum >= 1 && chNum <= 6) {
                ch[chNum - 1].activate();
                Serial.print(F("CH"));
                Serial.print(chNum);
                Serial.print(F(" ON at "));
                Serial.println(ch[chNum - 1].getIntensity());
            }
            return;
        }

        // CxIy — set intensity and activate
        if (iIdx > 0) {
            int chNum = cmd.substring(1, iIdx).toInt();
            String intStr = cmd.substring(iIdx + 1);

            // Validate: intensity string must be non-empty and contain only digits.
            // This prevents misparses like "C1ION" (→ toInt()=0 = MAX STIM) from firing.
            if (intStr.length() == 0) return;
            for (unsigned int k = 0; k < intStr.length(); k++) {
                if (!isDigit(intStr.charAt(k))) return;
            }

            int intensity = intStr.toInt();

            if (chNum >= 1 && chNum <= 6 && intensity >= 0 && intensity <= 255) {
                ch[chNum - 1].setIntensityAndActivate((uint8_t)intensity);
                Serial.print(F("CH"));
                Serial.print(chNum);
                Serial.print(F(" I="));
                Serial.println(intensity);
            }
            return;
        }

        return;  // Malformed C command — ignore
    }

    // ── Legacy single-char commands ────────────────────────────────────────
    char c = cmd.charAt(0);
    switch (c) {
        case '1':
            ch[0].isActive() ? ch[0].deactivate() : ch[0].activate();
            Serial.print(F("CH1 "));
            Serial.println(ch[0].isActive() ? F("ON") : F("OFF"));
            break;
        case '2':
            ch[1].isActive() ? ch[1].deactivate() : ch[1].activate();
            Serial.print(F("CH2 "));
            Serial.println(ch[1].isActive() ? F("ON") : F("OFF"));
            break;
        case 'q':
            if (legacyIntensity[0] > 0) {
                legacyIntensity[0]--;
                ch[0].setIntensity(legacyIntensity[0]);
                Serial.print(F("CH1 I="));
                Serial.println(legacyIntensity[0]);
            }
            break;
        case 'a':
            if (legacyIntensity[0] < 255) {
                legacyIntensity[0]++;
                ch[0].setIntensity(legacyIntensity[0]);
                Serial.print(F("CH1 I="));
                Serial.println(legacyIntensity[0]);
            }
            break;
        case 'w':
            if (legacyIntensity[1] > 0) {
                legacyIntensity[1]--;
                ch[1].setIntensity(legacyIntensity[1]);
                Serial.print(F("CH2 I="));
                Serial.println(legacyIntensity[1]);
            }
            break;
        case 's':
            if (legacyIntensity[1] < 255) {
                legacyIntensity[1]++;
                ch[1].setIntensity(legacyIntensity[1]);
                Serial.print(F("CH2 I="));
                Serial.println(legacyIntensity[1]);
            }
            break;
        default:
            break;
    }
}

// ── Main loop ──────────────────────────────────────────────────────────────
void loop() {
    // Process incoming serial commands
    if (Serial.available() > 0) {
        String msg = Serial.readStringUntil('\n');
        processCommand(msg);
    }

    // Safety timeout: kill everything if no command received for 15 seconds
    // Only triggers if at least one channel is active
    if (anyChannelActive()) {
        if (millis() - lastCommandTime >= TIMEOUT_MS) {
            if (!timeoutTriggered) {
                allOff();
                Serial.println(F("SAFETY TIMEOUT — ALL OFF (no command for 15s)"));
                timeoutTriggered = true;
            }
        }
    }
}
