class Login < HyperComponent

  render(DIV) do
    H1 { "Login" }

    if Rails.env == "production"
      HR{}
      H2 { "SSO providers" }
    else
      H2 { "SSO Doesn't work in development mode" }
    end
    
    HR{}
    H2 { "Local account" }
    FORM(method: "post", action: "/login") do
      CSRF{}
      
      INPUT(type: :text, placeholder: "email", name: "email")
      BR{}
      INPUT(type: :password, placeholder: "password", name: "password")
      BR{}
      
      BUTTON(type: "submit") { "Login" }
    end

    if App.acting_user
      DIV { "You are logged in!"}

      FORM(method: "post", action: "/logout") do
        CSRF{}
        BUTTON(type: "submit") { "Logout" }
      end
    end
  end
end
