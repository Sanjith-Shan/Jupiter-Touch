#ifndef AD5252_H
#define AD5252_H

#include <Arduino.h>
#include <Wire.h>

// AD5252 dual-channel digital potentiometer (I2C)
// Wiper indices: 1 = RDAC1, 3 = RDAC3
// Position 0   = minimum resistance = maximum EMS output
// Position 255 = maximum resistance = minimum EMS output (safe/off state)

class AD5252 {
public:
    AD5252(uint8_t i2cAddress);
    void setPosition(uint8_t wiperIndex, uint8_t position);
    uint8_t getPosition(uint8_t wiperIndex);

private:
    uint8_t _address;
};

#endif
