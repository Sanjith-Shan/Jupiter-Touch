#ifndef EMSCHANNEL_H
#define EMSCHANNEL_H

#include <Arduino.h>
#include "AD5252.h"

#define POT_OFF  255  // Max resistance — safe/off state, sent before relay opens
#define POT_MAX    0  // Min resistance — maximum stimulation

class EMSChannel {
public:
    // pin1, pin2: the two photo-relay control pins
    // poti: pointer to the AD5252 chip this channel belongs to
    // wiperIndex: 1 (RDAC1) or 3 (RDAC3)
    EMSChannel(uint8_t pin1, uint8_t pin2, AD5252* poti, uint8_t wiperIndex);

    void begin();

    // Activate relay (does not change intensity)
    void activate();

    // Set pot to POT_OFF, wait briefly, then open relay
    void deactivate();

    // Set raw pot value (0 = max stim, 255 = min stim)
    void setIntensity(uint8_t rawValue);

    // Activate relay and apply intensity in one call
    void setIntensityAndActivate(uint8_t rawValue);

    bool    isActive()       const;
    uint8_t getIntensity()   const;

private:
    uint8_t _pin1, _pin2;
    AD5252* _poti;
    uint8_t _wiperIndex;
    bool    _active;
    uint8_t _currentIntensity;
};

#endif
