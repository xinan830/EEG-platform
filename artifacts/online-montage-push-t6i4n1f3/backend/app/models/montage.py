"""Validated API schemas for user-defined linear EEG montages."""

from pydantic import BaseModel


class CustomMontageTermPayload(BaseModel):
    channel: str
    weight: float


class CustomMontageChannelPayload(BaseModel):
    name: str
    terms: list[CustomMontageTermPayload]
