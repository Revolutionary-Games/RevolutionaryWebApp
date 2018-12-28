class CreateUsers < ActiveRecord::Migration[5.2]
  def change
    create_table :users do |t|
      t.string :email
      t.boolean :local
      t.string :name
      t.string :sso_source
      t.boolean :developer
      t.boolean :admin
      t.string :password_digest

      t.timestamps
    end
  end
end
