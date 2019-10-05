class CurrentUser < HyperComponent

  render(DIV) do
    H1 { App.acting_user.email }

    P {
      "Here are your details:"
    }

    UserProperties(user: App.acting_user)
  end
end
