# frozen_string_literal: true

# Admin email section
class EmailOptions < HyperComponent
  before_mount do
    @email_send_status = ''
    @target_email_entered = false
    @target_email = ''
    @show_sending_spinner = false
  end

  render(DIV) do
    P { 'TODO: make a serverop that checks email is configured right' }
    P { 'TODO: make a serverop that returns some email statistics' }

    HR {}

    H2 { 'Test email' }

    P {
      'You can use this form to test email delivery.'
    }

    RS.Form() {
      RS.FormGroup {
        INPUT(type: :text, placeholder: 'email to send test to', name: 'email',
              value: @target_email)
          .on(:change) { |e|
          mutate {
            @target_email_entered = e.target.value != ''
            @target_email = e.target.value
          }
        }
      }

      if @email_send_status
        DIV {
          @email_send_status
        }
      end

      RS.Button(color: 'primary', disabled: !@target_email_entered) {
        SPAN { 'Send' }
        RS.Spinner(color: 'secondary', size: 'sm') if @show_sending_spinner
      }.on(:click) {
        mutate {
          @show_sending_spinner = true
          @target_email_entered = false
          @email_send_status = ''
        }

        SendTestEmail.run(email: @target_email)
                     .then {
          mutate {
            @email_send_status = 'Email sent'
            @show_sending_spinner = false
          }
        }.fail { |error|
          mutate {
            @email_send_status = "Failed to send email, error: #{error}"
            @show_sending_spinner = false
          }
        }
      }
    }
  end
end
