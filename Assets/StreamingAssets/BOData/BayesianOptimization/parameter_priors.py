from __future__ import annotations
from dataclasses import dataclass
from typing import Literal
import torch

class ParameterSpace:
    """
    Just a class too keep track of parameters. This is how I usually do this. I am sure pascal and mark have implemented something similar
    While the user might not work in unit space, we always should optimize in normalized conditions [0,1]. So make sure to normalize as soon as the user does something, 
    and unnormalize on rollouts when you return a value. 
    """
    def __init__(self, params: list[tuple[str, float, float]]) -> None:
        """
        On initialization pass a list of parameters and their ranges. 
        """
        self._names: list[str] = []
        self._lo: list[float] = []
        self._hi: list[float] = []

        for name, lo, hi in params:
            if hi <= lo:
                raise ValueError(f"Parameter '{name}': hi ({hi}) must be > lo ({lo})")
            self._names.append(name)
            self._lo.append(lo)
            self._hi.append(hi)

    # ------------------------------------------------------------------
    def _index(self, name: str) -> int:
        try:
            return self._names.index(name)
        except ValueError:
            raise ValueError(
                f"Unknown parameter '{name}'. Known: {self._names}"
            ) from None

    def _norm(self, idx: int, value: float) -> float:
        """Map a single raw value to [0, 1]."""
        return (value - self._lo[idx]) / (self._hi[idx] - self._lo[idx])

    def _norm_std(self, idx: int, std: float) -> float:
        """
        Scale a raw standard deviation to normalized space.
        We need normalize stds for our prior weights
        """
        return std / (self._hi[idx] - self._lo[idx])

    # ------------------------------------------------------------------
    def soft_bound(
        self,
        name: str,
        low: float,
        high: float,
        sharpness: float = 200.0,
    ) -> "ParameterPrior":
        """
        
        Basically, if you want to bound the sampling to a subset of the parameter space. However, it needs to be differentiable. Hence a softbox. 
        Smooth box prior expressed in raw parameter units. 

        Parameters
        ----------
        name      : parameter name (must match one passed to __init__)
        low, high : preferred region in raw units
        sharpness : sigmoid slope in normalized space (higher = harder wall), from testing 200 seems a good value. But too high could cause numerical issues so test this.
        """
        idx = self._index(name)
        return ParameterPrior.soft_bound(
            param_index=idx,
            low=self._norm(idx, low),
            high=self._norm(idx, high),
            sharpness=sharpness,
        )

    def gaussian(
        self,
        name: str,
        mean: float,
        std: float,
    ) -> "ParameterPrior":
        """
        Gaussian prior expressed in raw parameter units.

        Parameters
        ----------
        name      : parameter name (must match one passed to __init__)
        mean, std : preference center and spread in raw units
        """
        idx = self._index(name)
        return ParameterPrior.gaussian(
            param_index=idx,
            mean=self._norm(idx, mean),
            std=self._norm_std(idx, std),
        )


@dataclass
class ParameterPrior:
    """
    Differentiable prior weight for one parameter, operating in [0, 1] space.

    Do not construct directly — use the class-method factories or ParameterSpace:
        ParameterPrior.soft_bound(...) #normalized space
        ParameterPrior.gaussian(...) #normalized space
        ParameterSpace(...).soft_bound(...)   # raw units, recommended
        ParameterSpace(...).gaussian(...)     # raw units, recommended
    """

    param_index: int
    kind: Literal["soft_bound", "gaussian"]

    # soft_bound fields (normalized)
    low: float = 0.0
    high: float = 1.0
    sharpness: float = 200.0   # sigmoid steepness; too high can cause numerical issues

    # gaussian fields (normalized)
    mean: float = 0.5
    std: float = 0.2

    # ------------------------------------------------------------------
    @classmethod
    def soft_bound(
        cls,
        param_index: int,
        low: float,
        high: float,
        sharpness: float = 200.0,
    ) -> "ParameterPrior":
        """
        Smooth box prior in normalized [0, 1] space.

        Weight roughly 1 inside [low, high], tapers to 0 outside via sigmoid walls.

        Parameters
        ----------
        param_index : column index of the parameter in the candidate tensor
        low, high   : preferred region bounds, in normalized [0, 1]
        sharpness   : sigmoid slope; 20 ≈ 5 % transition width, higher = sharper
        """
        if not (0.0 <= low < high <= 1.0):
            raise ValueError(
                f"soft_bound: low and high must satisfy 0 ≤ low < high ≤ 1, "
                f"got low={low}, high={high}. "
                f"If your values are in raw units, use ParameterSpace.soft_bound() instead."
            )
        return cls(param_index=param_index, kind="soft_bound",
                   low=low, high=high, sharpness=sharpness)

    @classmethod
    def gaussian(
        cls,
        param_index: int,
        mean: float,
        std: float,
    ) -> "ParameterPrior":
        """
        Gaussian prior in normalized [0, 1] space.

        Weight = exp(-0.5 * ((x - mean) / std)^2).

        Parameters
        ----------
        param_index : column index of the parameter in the candidate tensor
        mean, std   : preference center and spread, in normalized [0, 1]
        """
        if std <= 0:
            raise ValueError(f"gaussian: std must be > 0, got {std}")
        if not (0.0 <= mean <= 1.0):
            raise ValueError(
                f"gaussian: mean must be in [0, 1], got {mean}. "
                f"If your values are in raw units, use ParameterSpace.gaussian() instead."
            )
        return cls(param_index=param_index, kind="gaussian", mean=mean, std=std)

    # ------------------------------------------------------------------
    def log_weight(self, X: torch.Tensor) -> torch.Tensor:
        """
        As our acquisition function is in logspace, we need to put our priors in log space. 
        X is basically sample points we are evaluating while optimizing our acquisition function.  
        """
        x = X[..., self.param_index]   # [..., q]

        if self.kind == "soft_bound":
            # log sigmoid(k*(x-lo)) + log sigmoid(k*(hi-x))
            # = -softplus(-k*(x-lo)) - softplus(-k*(hi-x))
            k = self.sharpness
            log_w = (
                - torch.nn.functional.softplus(-k * (x - self.low))
                - torch.nn.functional.softplus(-k * (self.high - x))
            )

        elif self.kind == "gaussian":
            z = (x - self.mean) / self.std
            log_w = -0.5 * z ** 2   # normalization constant omitted (irrelevant for optimization)

        else:
            raise ValueError(f"Unknown prior kind: {self.kind!r}")

        return log_w.sum(dim=-1)   # sum over q --> shape [...]
