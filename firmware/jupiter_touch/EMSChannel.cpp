#include "EMSChannel.h"

EMSChannel::EMSChannel(uint8_t pin1, uint8_t pin2, AD5252* poti, uint8_t wiperIndex)
    : _pin1(pin1), _pin2(pin2), _poti(poti), _wiperIndex(wiperIndex),
      _active(false), _currentIntensity(POT_OFF) {}

void EMSChannel::begin() {
    pinMode(_pin1, OUTPUT);
    pinMode(_pin2, OUTPUT);
    digitalWrite(_pin1, LOW);
    digitalWrite(_pin2, LOW);
    _poti->setPosition(_wiperIndex, POT_OFF);
    _active = false;
}

void EMSChannel::activate() {
    digitalWrite(_pin1, HIGH);
    digitalWrite(_pin2, HIGH);
    _active = true;
}

void EMSChannel::deactivate() {
    // Always ramp pot to safe value before opening relay
    _poti->setPosition(_wiperIndex, POT_OFF);
    _currentIntensity = POT_OFF;
    delay(10);
    digitalWrite(_pin1, LOW);
    digitalWrite(_pin2, LOW);
    _active = false;
}

void EMSChannel::setIntensity(uint8_t rawValue) {
    _currentIntensity = rawValue;
    _poti->setPosition(_wiperIndex, rawValue);
}

void EMSChannel::setIntensityAndActivate(uint8_t rawValue) {
    _poti->setPosition(_wiperIndex, rawValue);
    _currentIntensity = rawValue;
    if (!_active) {
        activate();
    }
}

bool EMSChannel::isActive() const {
    return _active;
}

uint8_t EMSChannel::getIntensity() const {
    return _currentIntensity;
}
