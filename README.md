# THYNK SMS Alert Notification System

This document outlines the SMS notification system implemented for THYNK, which sends emergency alerts to registered community users via SMS.

## Overview

The SMS notification system uses Vonage to send text messages automatically to community users whenever an LGU or Admin creates an alert. The system is designed to:

1. Send SMS notifications to all relevant community users based on their location
2. Format messages appropriately based on alert severity
3. Handle large batches of SMS with proper rate limiting
4. Provide error handling and logging

## Setup Instructions

### 1. Sign up for Vonage

1. Create a Vonage account at [https://dashboard.nexmo.com/sign-up](https://dashboard.nexmo.com/sign-up)
2. Get your API Key and API Secret from the Vonage dashboard
3. Set up a virtual number or a sender name (branded sender ID)

### 2. Configure the Application

1. Update the `appsettings.json` file with your Vonage credentials:
   ```json
   "Vonage": {
     "ApiKey": "YOUR_VONAGE_API_KEY",
     "ApiSecret": "YOUR_VONAGE_API_SECRET",
     "From": "THYNK"
   }
   ```

2. The "From" parameter can be either:
   - A virtual number (in international format)
   - An alphanumeric sender ID (e.g., "THYNK")

### 3. Using the SMS Notification System

The system will automatically send SMS notifications to users when:
- An LGU creates a new alert
- An Admin creates a new alert

The system handles:
- Properly formatting phone numbers to international format
- Error logging and reporting
- Sending messages in batches to avoid rate limits

## Testing the System

1. Register a user with a valid mobile number
2. Create a new alert through the LGU or Admin interface
3. The system will automatically send SMS notifications to all affected users

## Troubleshooting

Common issues and solutions:

1. SMS not being sent:
   - Check the Vonage dashboard for API errors
   - Verify your API key and secret are correct
   - Check the logs for detailed error messages

2. Phone number formatting issues:
   - Ensure phone numbers follow the format: country code + number (e.g., 639XXXXXXXXX)
   - The system automatically formats Philippine numbers starting with 09 or 9

## Additional Information

- **Sender ID**: Vonage allows you to set a custom alphanumeric sender ID (e.g., "THYNK")
- **Phone Number Format**: Phone numbers are stored in E.164 format (e.g., +639123456789)
- **Rate Limiting**: The service sends SMS in batches of 50 with 1-second delays

## Code Components

### SMS Service

- `VonageSMSService` in `Services/SMSService.cs` - Handles the actual SMS sending via Vonage API

### Alert Notification

- `AlertNotificationService` in `Services/AlertNotificationService.cs` - Processes alerts and determines which users should receive notifications

### Controllers

Both admin and LGU controllers have been updated to use the SMS notification service when creating alerts.

## Verifying Phone Numbers

During account registration, users must provide their phone number in the format: 9XXXXXXXXX. The system automatically adds the Philippines country code (+63).

## Testing SMS

To test the SMS functionality, you can:

1. Create a new alert in the LGU or Admin dashboard
2. Check the application logs for SMS delivery status
3. Verify that test phones receive the messages

**Note:** Vonage has better support for Philippines numbers compared to Twilio, with fewer restrictions during testing.

## Production Considerations

1. Monitor Vonage credits and usage
2. Consider implementing an opt-out mechanism for users
3. Add a dedicated dashboard for monitoring SMS delivery status
4. Implement SMS throttling if sending to large numbers of users

## Advantages of Vonage over Twilio

1. Better support for Philippines numbers
2. Fewer restrictions during testing
3. Support for alphanumeric sender IDs
4. Extended delivery reports
5. Often better pricing for Asia-Pacific regions

## Troubleshooting

If SMS are not being delivered:
1. Check application logs for errors
2. Verify Vonage credentials are correct
3. Ensure phone numbers are in valid format
4. Check Vonage console for delivery status 