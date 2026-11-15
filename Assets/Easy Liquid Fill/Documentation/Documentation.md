# Easy Liquid Fill

### User Manual & Documentation

**Version 1.0**
**Created by Ahmed Benlakhdhar**

---

### **Table of Contents**
1.  Introduction
2.  Quick Start Guide
3.  Core Components
4.  Public API
5.  Configuration
6.  FAQ & Support

---

### **1. Introduction**

Thank you for choosing Easy Liquid Fill! This asset is a lightweight and powerful solution for creating beautiful, physics-based, and interactive 2D liquid meters with a dynamic 3D swirling effect for your Unity project's UI.

**Key Features:**
*   **Dynamic Swirling Effect:** The liquid surface tilts and swirls in a circular, decaying oscillation in response to value changes or manual triggers.
*   **Highly Customizable:** Control fill speed, swirl intensity, decay, and oscillation frequency.
*   **Artistic Control:** Adjust liquid and surface colors, surface highlight intensity, and the base tilt of the liquid.
*   **Easy to Use:** A simple and clean public API.
*   **Full C# Source Code Included:** Clean, refactored, and ready to be extended.

---

### **2. Quick Start Guide**

The system is designed to be added to your project in seconds.

1.  **Add the Prefab:**
    Drag the `Globe.prefab` from the `Easy Liquid Fill/Prefabs/` folder into your UI Canvas.

2.  **Get a Reference:**
    In your own script, get a reference to the `LiquidAnimationController` component.
    ```csharp
    public LiquidAnimationController myHealthBar;
    ```

3.  **Control from Code:**
    Change the `fillAmount` property to update the meter, or call `TriggerSwirl()` to make it react to game events.
    ```csharp
    // Set the fill to 75%
    myHealthBar.fillAmount = 0.75f;

    // Trigger a half-strength swirl when the player is hit
    myHealthBar.TriggerSwirl(1f);
    ```

**Done!** The liquid will animate and swirl automatically.

*(See the Demo Scene in `Assets/Easy Liquid Fill/Demo` for a live example.)*

---

### **3. Core Components**

*   **`LiquidAnimationController`:** The main "brain" of the system. You interact with this component from your scripts. It handles all physics, timing, and animation.

*   **`LiquidAnimator`:** The visual "renderer" for the effect. It is controlled automatically by the `LiquidAnimationController`.

*   **`Globe` Prefab:** A pre-configured GameObject with both components, a circular mask, and all necessary child objects.

---

### **4. Public API**

Control the liquid's behavior from your own scripts using these public properties and methods on the `LiquidAnimationController`.

**Properties:**
*   `float fillAmount`: The target fill level (0.0 to 1.0).
*   `float fillDuration`: Time in seconds for the fill animation.
*   `float originalInclination`: The base tilt of the liquid surface.
*   `float maxTiltAmount`: The maximum swirl angle.
*   `float swirlDuration`: Time in seconds for the swirl to settle.
*   `float swirlSpeed`: The frequency of the swirl oscillation.

**Methods:**
*   `TriggerSwirl(float intensity)`: Manually triggers the swirling animation. The `intensity` parameter controls the strength of the swirl.

---

### **5. Configuration**

All customization is done directly in the Unity Inspector. Select the `Globe` object in your scene to access the two main components:

*   **On `LiquidAnimationController`:** Adjust all timing, physics, and color properties.
*   **On `LiquidAnimator`:** Adjust advanced visual properties like `Surface Tint Strength` and `Surface Angle Multiplier`.

---

### **6. FAQ & Support**

**Q: The liquid isn't moving or showing up!**
A: Make sure the `LiquidAnimator` component is correctly assigned to the `Liquid Animator` field on the `LiquidAnimationController` in the Inspector.

**Q: How do I make the liquid swirl when my player gets hit?**
A: From your player's health script, simply call the public method with an intensity value, for example: `myHealthBar.TriggerSwirl(0.8f);`

**Contact for support or feature requests:**
*   LinkedIn: https://www.linkedin.com/in/ahmedbenlakhdhar
*   ArtStation: https://www.artstation.com/ahmedbenlakhdhar