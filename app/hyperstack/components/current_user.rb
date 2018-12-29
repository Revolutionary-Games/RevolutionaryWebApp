class CurrentUser < HyperComponent

  render(DIV) do
    H1 { App.acting_user.email }

    P {
      "Here are your details:"
    }

    P { "name: #{ App.acting_user.name}" }
    P { "admin: #{ App.acting_user.admin}" }
    P { "developer: #{ App.acting_user.developer}" }
    P { "sso_source: #{ App.acting_user.sso_source}" }
    P { "local: #{ App.acting_user.local}" }
    P { "api token: #{ App.acting_user.api_token}" }
  end
end
