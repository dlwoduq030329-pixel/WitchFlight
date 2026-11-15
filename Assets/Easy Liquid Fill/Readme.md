# Easy Liquid Fill v1.0 by Ahmed Benlakhdhar

A physics-based system for creating beautiful, 2D liquid meters with a dynamic 3D swirling effect for Unity UI.

## Quick Start

1.  **Add the Prefab:**
    - Drag the `Globe.prefab` from `Easy Liquid Fill/Prefabs/` into your UI Canvas.

2.  **Control from Script:**
    - Get a reference to the `LiquidAnimationController` component.
    - Change the `fillAmount` property to update the meter.
    - Call `TriggerSwirl(1f)` to manually trigger a full-strength swirl.

Done. The liquid will animate and swirl automatically.
(See the Demo Scene and Documentation for more examples).

## Key Features

- **Dynamic Swirling Effect:** The liquid surface tilts and swirls in a circular, decaying oscillation in response to value changes or manual triggers.
- **Highly Customizable:** Control fill speed, swirl intensity, decay, and oscillation frequency.
- **Artistic Control:** Adjust liquid and surface colors, surface highlight intensity, and the base tilt of the liquid.
- **Easy to Use:** A simple and clean public API.
- **Full C# Source Code Included:** Clean, refactored, and ready to be extended.

## Configuration

Select the `LiquidFillGlobe` object in your scene and adjust the properties on the **`LiquidAnimationController`** and **`LiquidAnimator`** components in the Inspector.

## Support

For the full manual, see the Documentation folder. For support or feature requests:
- LinkedIn: https://www.linkedin.com/in/ahmedbenlakhdhar
- ArtStation: https://www.artstation.com/ahmedbenlakhdhar