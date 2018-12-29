# app/hyperstack/component/app.rb

class CSRF < HyperComponent
  render do
    INPUT("input", type: "hidden", name: "authenticity_token",
          value: Hyperstack::ClientDrivers.opts[:form_authenticity_token])
  end
end

# This is your top level component, the rails router will
# direct all requests to mount this component.  You may
# then use the Route psuedo component to mount specific
# subcomponents depending on the URL.

class App < HyperComponent
  include Hyperstack::Router

  def self.acting_user
    User.find(Hyperstack::Application.acting_user_id) if Hyperstack::Application.acting_user_id
  end
  
  render(DIV) do

    DIV do
      UL do
        LI { Link('/') { 'Home' } }
        LI { Link('/submit') { 'Submit a crash' } }        
        LI { Link('/login') { 'Login' } } if !App.acting_user
        LI { Link('/symbols') { 'Symbols' } } if App.acting_user
        LI { Link('/about') { 'About' } }
        # LI { Link('/builds') { 'Releases / Previews' } }
        LI { Link('/users') { 'Users' } } if App.acting_user&.admin
        LI { Link('/logout') { 'Logout' } } if App.acting_user
      end
    end

    DIV do
      SPAN { "Welcome " }
      Link('/me') { App.acting_user.email }
    end if App.acting_user

    DIV(class: "Content") do
      Switch do
        Route('/symbols', mounts: Symbols)
        Route('/', exact: true, mounts: Home)
        Route('/login', exact: true, mounts: Login)
        Route('/logout', mounts: Logout)
        Route('/about', mounts: About)
        Route('/users', mounts: Users)
        Route('/me', mounts: CurrentUser)
        Route('/submit', mounts: Submit)
      end
    end

    Footer{}
  end
end
