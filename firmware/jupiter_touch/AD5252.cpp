#include "AD5252.h"

AD5252::AD5252(uint8_t i2cAddress) : _address(i2cAddress) {}

void AD5252::setPosition(uint8_t wiperIndex, uint8_t position) {
    Wire.beginTransmission(_address);
    Wire.write(wiperIndex);
    Wire.write(position);
    Wire.endTransmission(1);
}

uint8_t AD5252::getPosition(uint8_t wiperIndex) {
    Wire.beginTransmission(_address);
    Wire.write(wiperIndex);
    Wire.endTransmission();
    Wire.requestFrom(_address, (uint8_t)1);
    return Wire.read();
}
