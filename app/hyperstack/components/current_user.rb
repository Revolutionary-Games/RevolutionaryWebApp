# frozen_string_literal: true

class CurrentUser < HyperComponent
  # To quiet some react warnings
  include Hyperstack::Router::Helpers

  render(DIV) do
    unless App.acting_user
      P { 'No current user' }
      return
    end

    H1 { App.acting_user.email }

    P {
      'Here are your details:'
    }

    UserProperties(user: App.acting_user)
  end
end
