class Logout < HyperComponent

  render(DIV) do
    H1 { "Logout" }

    H1 { "You don't seem to be logged in" } if !App.acting_user
    
    HR{}
    H2 { "If you are sure you want to logout press this" }

    FORM(method: "post", action: "/logout") do
      CSRF{}
      BUTTON(type: "submit") { "Logout" }

    end
  end
end
