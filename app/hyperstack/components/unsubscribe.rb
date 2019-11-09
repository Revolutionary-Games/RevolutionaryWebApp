# frozen_string_literal: true

# Component for unsubscribe pages
class Unsubscribe < HyperComponent
  include Hyperstack::Router::Helpers

  render(DIV) do
    type = match.params[:type]

    case type
    when 'email'
      H1 { 'Unsubscribe from ThriveDevCenter emails' }

      key = match.params[:key]

      unless key
        H2 { 'Unsubscribe key is missing' }
        return
      end

      P {
        'Click the button if you are sure you want to unsubscribe.' \
        'You may not be able to resubscribe to whatever emails you are unsubscribing from.'
      }

      P { @status_text } if @status_text

      BUTTON { 'Unsubscribe' }.on(:click) {
        ProcessEmailUnsubscribeRequest
          .run(key: key)
          .then { mutate @status_text = 'Unsubscribe successful!' }
          .fail { |error| mutate @status_text = "Failed! error: #{error}" }
      }

    else
      H1 { 'Unknown type of thing to unsubscribe from' }
      return
    end
  end
end
