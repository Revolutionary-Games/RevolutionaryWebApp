class CreateLfsProjects < ActiveRecord::Migration[5.2]
  def change
    create_table :lfs_projects do |t|
      t.string :name
      t.string :slug
      t.boolean :public

      t.timestamps
    end
  end
end
